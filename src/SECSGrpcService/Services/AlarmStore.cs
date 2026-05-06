using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
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
    private readonly ConcurrentDictionary<string, ActiveAlarm> _active = new();

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
        var activeAlarm = new ActiveAlarm
        {
            Source = source,
            AlarmId = request.AlarmId,
            AlarmCode = request.AlarmCode ?? ByteString.Empty,
            AlarmText = request.AlarmText ?? string.Empty,
            Severity = (int)request.Severity == 0 ? (AlarmSeverity)2 : request.Severity,
            OccurredAtUnixMs = request.OccurredAtUnixMs
        };

        _active[Key(source, request.AlarmId)] = activeAlarm;

        try
        {
            using var db = _dbContextFactory.CreateDbContext();

            // 使用 LINQ 生成可追溯时间戳（保持“通过 LINQ 方式处理数据”的风格）
            var occurredAt = new[] { request.OccurredAtUnixMs }.Select(x => x).FirstOrDefault();

            var history = new AlarmHistoryRecord
            {
                Source = source,
                AlarmId = request.AlarmId,
                AlarmCode = (request.AlarmCode ?? ByteString.Empty).ToByteArray(),
                AlarmText = request.AlarmText ?? string.Empty,
                Severity = (int)activeAlarm.Severity,
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
    public bool Remove(string source, uint alarmId)
    {
        var safeSource = string.IsNullOrWhiteSpace(source) ? "<unknown>" : source.Trim();
        return _active.TryRemove(Key(safeSource, alarmId), out _);
    }

    /// <summary>
    /// 查询激活报警。
    /// source 为空时返回全部；否则按来源过滤。
    /// </summary>
    public IEnumerable<ActiveAlarm> Query(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return _active.Values.ToArray();

        var safeSource = source.Trim();
        return _active.Values.Where(x => string.Equals(x.Source, safeSource, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
}
