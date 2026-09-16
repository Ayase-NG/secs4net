using GY.Server;
using Microsoft.EntityFrameworkCore;
using Nacos.V2.Utils;
using System.Collections.Concurrent;

namespace SECSGrpcService.Services;

/// <summary>
/// 报警内存存储。
/// 用于在 gRPC 服务进程内保存“当前激活报警”状态，
/// 供 <c>ReportAlarm</c> / <c>ClearAlarm</c> / <c>QueryActiveAlarms</c> 共享使用。
/// </summary>
public sealed class AlarmStore
{
    private readonly IDbContextFactory<TraceabilityDbContext> _dbContextFactory;
    private readonly ILogger<AlarmStore> _logger;

    /// <summary>
    /// 激活报警表：Key = "{source}:{alarmId}"，Value = 报警详情。
    /// 使用并发字典保证多线程读写安全。
    /// </summary>
    private readonly ConcurrentDictionary<uint, AlarmSatus> _active = new();

    public AlarmStore(IDbContextFactory<TraceabilityDbContext> dbContextFactory, ILogger<AlarmStore> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;

        try
        {
            using var db = _dbContextFactory.CreateDbContext();
            db.Database.EnsureCreated();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize alarm history database.");
        }
    }

    /// <summary>
    /// 生成报警唯一键。
    /// </summary>
    private static string Key(string source, uint alarmId) => $"{source}:{alarmId}";

    /// <summary>
    /// 新增或更新激活报警。
    /// 若相同 source + alarmId 已存在，则覆盖为最新内容。
    /// 同时写入报警历史表，用于后续分析与追溯。
    /// </summary>
    public void Upsert(AlarmReportRequest request)
    {
        var source = string.IsNullOrWhiteSpace(request.Source) ? "<unknown>" : request.Source.Trim();
        // 将 gRPC int32 字段统一转换为当前报警存储模型使用的无符号 ID 和单字节代码。
        uint alarmId = unchecked((uint)request.AlarmId);
        byte alarmCode = unchecked((byte)request.AlarmCode);
        var activeAlarm = new AlarmSatus
        {
            AlarmId = alarmId,
            AlarmCode = (byte)(0x1000_0000 | alarmCode), // 将报警代码放在高位，确保与历史记录一致
            AlarmText = request.AlarmText ?? string.Empty,
            ALACT = false,
        };
        _active.AddOrUpdate(alarmId, activeAlarm, (key, oldvalue) => activeAlarm);

        try
        {
            using var db = _dbContextFactory.CreateDbContext();

            // 使用 LINQ 生成可追溯时间戳（保持“通过 LINQ 方式处理数据”的风格）
            var occurredAt = new[] { request.OccurredAtUnixMs }.Select(x => x).FirstOrDefault();
            // 将报警历史记录写入数据库
            var history = new AlarmHistoryRecord
            {
                Source = source,
                AlarmId = alarmId,
                AlarmCode = new[] { alarmCode },
                AlarmText = request.AlarmText ?? string.Empty,
                OccurredAtUnixMs = occurredAt,
                CreatedAtUtc = DateTime.UtcNow
            };

            db.AlarmHistories.Add(history);
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist alarm history. Source={Source}, AlarmId={AlarmId}", source, request.AlarmId);
        }
    }

    /// <summary>
    /// 清除指定报警。
    /// </summary>
    /// <returns>成功删除返回 true；不存在返回 false。</returns>
    public bool Remove(uint alarmId)
    {
        if (!_active.TryGetValue(alarmId,out var al))
            return false;
        al.ALACT = false; // 标记为已确认
        al.AlarmCode = 0x0000_0000; // 清除报警代码
        return _active.TryUpdate(alarmId, al, _active.GetValueOrDefault(alarmId));
    }

    /// <summary>
    /// 查询激活报警。
    /// </summary>
    public List<AlarmSatus> GetAlarmList()
    {
        return _active.Values.ToList();
    }
}

public class AlarmSatus
{
    // 状态
    public byte AlarmCode { get; set; } = 0x0000_0000;
    // 编号
    public uint AlarmId { get; set; } = 0;
    // 描述
    public string AlarmText { get; set; } = string.Empty;
    // Host是否已确认
    public bool ALACT { get; set; } = false;
}
