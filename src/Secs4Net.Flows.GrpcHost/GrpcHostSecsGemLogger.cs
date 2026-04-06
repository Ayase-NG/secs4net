using Microsoft.Extensions.Logging;
using Secs4Net;
using Secs4Net.Sml;

namespace Secs4Net.Flows.GrpcHost;

/// <summary>
/// 将 SECS 报文轨迹写入 ASP.NET Core 日志（与 samples/DeviceWorkerService 中 DeviceLogger 思路一致）。
/// </summary>
internal sealed class GrpcHostSecsGemLogger(ILogger<GrpcHostSecsGemLogger> logger) : ISecsGemLogger
{
    public void MessageIn(SecsMessage msg, int id) =>
        logger.LogTrace($"<-- [0x{id:X8}] {msg.ToSml()}");

    public void MessageOut(SecsMessage msg, int id) =>
        logger.LogTrace($"--> [0x{id:X8}] {msg.ToSml()}");

    public void Debug(string msg) => logger.LogDebug("{Msg}", msg);
    public void Info(string msg) => logger.LogInformation("{Msg}", msg);
    public void Warning(string msg) => logger.LogWarning("{Msg}", msg);
    public void Error(string msg) => Error(msg, message: null, ex: null);
    public void Error(string msg, Exception ex) => Error(msg, message: null, ex);
    public void Error(string msg, SecsMessage? message, Exception? ex) =>
        logger.LogError(ex, "{Msg} {Message}", msg, message);
}
