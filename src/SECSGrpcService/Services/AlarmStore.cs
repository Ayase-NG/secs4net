using Google.Protobuf;
using System.Collections.Concurrent;

namespace SECSGrpcService.Services;

/// <summary>
/// 报警内存存储。
/// 用于在 gRPC 服务进程内保存“当前激活报警”状态，
/// 供 <c>ReportAlarm</c> / <c>ClearAlarm</c> / <c>QueryActiveAlarms</c> 共享使用。
/// </summary>
internal static class AlarmStore
{
    /// <summary>
    /// 激活报警表：Key = "{source}:{alarmId}"，Value = 报警详情。
    /// 使用并发字典保证多线程读写安全。
    /// </summary>
    private static readonly ConcurrentDictionary<string, ActiveAlarm> _active = new();

    /// <summary>
    /// 生成报警唯一键。
    /// </summary>
    private static string Key(string source, uint alarmId) => $"{source}:{alarmId}";

    /// <summary>
    /// 新增或更新激活报警。
    /// 若相同 source + alarmId 已存在，则覆盖为最新内容。
    /// </summary>
    public static void Upsert(AlarmReportRequest request)
    {
        var source = string.IsNullOrWhiteSpace(request.Source) ? "<unknown>" : request.Source.Trim();
        _active[Key(source, request.AlarmId)] = new ActiveAlarm
        {
            Source = source,
            AlarmId = request.AlarmId,
            AlarmCode = request.AlarmCode ?? ByteString.Empty,
            AlarmText = request.AlarmText ?? string.Empty,
            Severity = (int)request.Severity == 0 ? (AlarmSeverity)2 : request.Severity,
            OccurredAtUnixMs = request.OccurredAtUnixMs
        };
    }

    /// <summary>
    /// 清除指定报警。
    /// </summary>
    /// <returns>成功删除返回 true；不存在返回 false。</returns>
    public static bool Remove(string source, uint alarmId)
    {
        var safeSource = string.IsNullOrWhiteSpace(source) ? "<unknown>" : source.Trim();
        return _active.TryRemove(Key(safeSource, alarmId), out _);
    }

    /// <summary>
    /// 查询激活报警。
    /// source 为空时返回全部；否则按来源过滤。
    /// </summary>
    public static IEnumerable<ActiveAlarm> Query(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return _active.Values.ToArray();

        var safeSource = source.Trim();
        return _active.Values.Where(x => string.Equals(x.Source, safeSource, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
}
