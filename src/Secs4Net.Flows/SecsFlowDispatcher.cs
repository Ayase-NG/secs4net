using Secs4Net;

namespace Secs4Net.Flows;

/// <summary>
/// 协调「客制化装配」与 <see cref="ISecsGem.SendAsync"/>，完成从逻辑请求到 Fab 侧 HSMS 发送。
/// </summary>
public sealed class SecsFlowDispatcher(IFabFlowProfile profile)
{
    private readonly IFabFlowProfile _profile = profile;

    /// <summary>
    /// 发送主消息并在需要时等待次消息；会正确释放 <see cref="SecsMessage"/> 资源。
    /// </summary>
    public async Task<FlowSendResult> SendPrimaryAsync(ISecsGem gem, FlowSendRequest request, CancellationToken cancellation)
    {
        using var primary = _profile.BuildPrimaryMessage(request);
        SecsMessage? reply = null;
        try
        {
            reply = await gem.SendAsync(primary, cancellation).ConfigureAwait(false);

            if (!primary.ReplyExpected)
            {
                return new FlowSendResult(true, null, null, null, null);
            }

            if (reply is null)
            {
                return new FlowSendResult(false, "未收到次消息（ReplyExpected 为 true 时回复不应为空）。", null, null, null);
            }

            return new FlowSendResult(true, null, reply.S, reply.F, reply.ToString());
        }
        catch (Exception ex)
        {
            return new FlowSendResult(false, ex.Message, null, null, null);
        }
        finally
        {
            reply?.Dispose();
        }
    }
}
