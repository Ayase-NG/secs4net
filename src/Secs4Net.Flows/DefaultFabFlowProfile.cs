using Secs4Net;

namespace Secs4Net.Flows;

/// <summary>
/// 不绑定具体 Fab 的默认装配：按 S/F/Name 构造 <see cref="SecsMessage"/>，可选附带一条 ASCII Item。
/// </summary>
public class DefaultFabFlowProfile : IFabFlowProfile
{
    /// <inheritdoc />
    public virtual string ProfileId => "default";

    /// <inheritdoc />
    public virtual SecsMessage BuildPrimaryMessage(FlowSendRequest request)
    {
        var message = new SecsMessage(request.Stream, request.Function, request.ReplyExpected)
        {
            Name = request.Name,
        };

        if (!string.IsNullOrEmpty(request.AsciiPayload))
        {
            message.SecsItem = Item.A(request.AsciiPayload);
        }

        return message;
    }
}
