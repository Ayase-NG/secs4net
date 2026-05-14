using Secs4Net;
using SECSbuilder;
using SECSdata;
using SECShandler.Interfaces;

namespace SECSGrpcService.Services;

/// <summary>
/// 主动外发 SxFy 分发器实现。
/// 说明：统一承载设备主动上报消息的发送、会话可用性检查与基础重试。
/// </summary>
public sealed class ActiveSxFyDispatcher : IActiveSxFyDispatcher
{
    private readonly SecsGemContext _secsGemContext;
    private readonly ILogger<ActiveSxFyDispatcher> _logger;

    public ActiveSxFyDispatcher(
        SecsGemContext secsGemContext,
        ILogger<ActiveSxFyDispatcher> logger)
    {
        _secsGemContext = secsGemContext;
        _logger = logger;
    }

    /// <summary>
    /// 发送 S6F11（Event Report Send）。
    /// 策略：会话可用时最多尝试 2 次，失败则返回 false。
    /// </summary>
    public async Task<bool> SendS6F11Async(S6F11_data data, CancellationToken cancellationToken)
    {
        // if 关键分支：参数为空时快速失败，避免后续空引用。
        if (data is null)
        {
            return false;
        }

        // for 关键分支：执行最多 2 次发送尝试。
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            // if 关键分支：当前无活动会话则直接失败，不进入发送。
            if (!_secsGemContext.TryGet(out var secsGem) || secsGem is null)
            {
                _logger.LogWarning("SECS session not available. Skip S6F11 send. CEID={CEID}, Attempt={Attempt}", data.CEID, attempt);
                return false;
            }

            try
            {
                // 方法关键节点：由 builder 统一编码 S6F11 消息体。
                var s6f11 = S6F11_builder.Build(data);
                var reply = await secsGem.SendAsync(s6f11, cancellationToken).ConfigureAwait(false);

                // if 关键分支：收到 S6F12 视为发送闭环成功。
                if (reply is not null && reply.S == 6 && reply.F == 12)
                {
                    _logger.LogInformation("S6F11 sent and S6F12 received. CEID={CEID}, Attempt={Attempt}", data.CEID, attempt);
                    return true;
                }

                // 非严格分支：未收到 S6F12 也记录告警，允许进入下一次重试。
                _logger.LogWarning("S6F11 sent but S6F12 not returned (actual S{S}F{F}). CEID={CEID}, Attempt={Attempt}", reply?.S, reply?.F, data.CEID, attempt);
            }
            catch (SecsException ex)
            {
                _logger.LogWarning(ex, "S6F11 send timeout or SECS error. CEID={CEID}, Attempt={Attempt}", data.CEID, attempt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "S6F11 send failed. CEID={CEID}, Attempt={Attempt}", data.CEID, attempt);
            }

            // if 关键分支：仅在还有剩余次数时记录重试提示。
            if (attempt < 2)
            {
                _logger.LogWarning("S6F11 send will retry. CEID={CEID}", data.CEID);
            }
        }

        // 方法兜底：重试耗尽仍失败。
        _logger.LogError("S6F11 retry exhausted. CEID={CEID}", data.CEID);
        return false;
    }
}
