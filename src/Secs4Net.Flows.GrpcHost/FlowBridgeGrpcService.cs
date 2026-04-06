using Grpc.Core;
using Secs4Net.Flows;
using Secs4Net.Flows.GrpcHost.Protos;

namespace Secs4Net.Flows.GrpcHost;

/// <summary>
/// gRPC 入口：将 <see cref="SendPrimaryRequest"/> 转为 <see cref="FlowSendRequest"/>，再经 <see cref="SgrsFabFlowProfile"/> 装配为 SECS 报文发往 Fab。
/// </summary>
public sealed class FlowBridgeGrpcService : Protos.FlowBridge.FlowBridgeBase
{
    private readonly SecsFlowDispatcher _dispatcher;
    private readonly ISecsGem _secsGem;

    public FlowBridgeGrpcService(SecsFlowDispatcher dispatcher, ISecsGem secsGem)
    {
        _dispatcher = dispatcher;
        _secsGem = secsGem;
    }

    /// <inheritdoc />
    public override async Task<SendPrimaryReply> SendPrimary(SendPrimaryRequest request, ServerCallContext context)
    {
        if (request.Stream > 127)
        {
            return new SendPrimaryReply { Ok = false, Error = "stream 必须 ≤ 127（SECS-II 流号范围）。" };
        }

        if (request.Function > 255)
        {
            return new SendPrimaryReply { Ok = false, Error = "function 必须 ≤ 255。" };
        }

        var flow = new FlowSendRequest(
            (byte)request.Stream,
            (byte)request.Function,
            request.ReplyExpected,
            string.IsNullOrEmpty(request.Name) ? null : request.Name,
            string.IsNullOrEmpty(request.AsciiPayload) ? null : request.AsciiPayload);

        var result = await _dispatcher.SendPrimaryAsync(_secsGem, flow, context.CancellationToken).ConfigureAwait(false);

        return new SendPrimaryReply
        {
            Ok = result.Ok,
            Error = result.Error ?? string.Empty,
            ReplyStream = result.ReplyStream ?? 0,
            ReplyFunction = result.ReplyFunction ?? 0,
            ReplySummary = result.ReplySummary ?? string.Empty,
        };
    }
}
