namespace Secs4Net.Flows;

/// <summary>
/// 将主消息发往设备后的结果摘要（便于 gRPC / HTTP 层返回给上游）。
/// </summary>
public sealed record FlowSendResult(
    bool Ok,
    string? Error,
    byte? ReplyStream,
    byte? ReplyFunction,
    /// <summary>回复消息的 <see cref="object.ToString"/> 摘要；完整 SML 请在 Host 侧日志中查看。</summary>
    string? ReplySummary);
