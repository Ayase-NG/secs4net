using Grpc.Core;

namespace SECSGrpcService.Services
{
    /// <summary>
    /// EFEM gRPC 服务实现，对应 <c>efem.proto</c> 中定义的接口。
    /// </summary>
    public sealed class SecsEfemGrpc : global::SECSGrpcService.SECSGrpcService.SECSGrpcServiceBase
    {
        private readonly ILogger<SecsEfemGrpc> _logger;

        public SecsEfemGrpc(ILogger<SecsEfemGrpc> logger)
        {
            _logger = logger;
        }

        public override Task<EFEMReply> StartMeasurement(StartMessage request, ServerCallContext context)
        {
            var slots = request.SlotsList?.Count > 0 ? string.Join(",", request.SlotsList) : "<empty>";
            _logger.LogInformation(
                "StartMeasurement received. Name={Name}, LotId={LotId}, PPID={PPID}, Slots=[{Slots}], Peer={Peer}",
                request.Name,
                request.LotId,
                request.PPID,
                slots,
                context.Peer);

            return Task.FromResult(new EFEMReply
            {
                MessageCode = 0,
                Message = $"StartMeasurement OK: lotId={request.LotId}, ppid={request.PPID}, slots={slots}"
            });
        }

        public override Task<EFEMReply> StopMeasurement(WaferMessage request, ServerCallContext context)
        {
            var rawSlot = request?.SlotId?.Trim() ?? string.Empty;
            _logger.LogInformation("StopMeasurement received. SlotId={SlotId}, Peer={Peer}", rawSlot, context.Peer);

            if (int.TryParse(rawSlot, out var slot) && slot >= 0 && slot <= 25)
            {
                return Task.FromResult(new EFEMReply
                {
                    MessageCode = 0,
                    Message = string.Empty
                });
            }

            return Task.FromResult(new EFEMReply
            {
                MessageCode = 1,
                Message = "晶圆序号错误"
            });
        }

        public override Task<EFEMReply> PauseMeasurement(WaferMessage request, ServerCallContext context)
            => HandleWaferActionAsync(nameof(PauseMeasurement), request, context);

        public override Task<EFEMReply> ResumeMeasurement(WaferMessage request, ServerCallContext context)
            => HandleWaferActionAsync(nameof(ResumeMeasurement), request, context);

        public override Task<EFEMReply> ProcessProgramSelect(RecipeMessage request, ServerCallContext context)
        {
            var ppName = string.IsNullOrWhiteSpace(request.PPName) ? "<empty>" : request.PPName;
            var ppid = string.IsNullOrWhiteSpace(request.PPID) ? "<empty>" : request.PPID;

            _logger.LogInformation(
                "ProcessProgramSelect received. PPName={PPName}, PPID={PPID}, Peer={Peer}",
                ppName,
                ppid,
                context.Peer);

            return Task.FromResult(new EFEMReply
            {
                MessageCode = 0,
                Message = $"ProcessProgramSelect OK: PPName={ppName}, PPID={ppid}"
            });
        }

        private Task<EFEMReply> HandleWaferActionAsync(string action, WaferMessage request, ServerCallContext context)
        {
            var slotId = string.IsNullOrWhiteSpace(request.SlotId) ? "<empty>" : request.SlotId;
            _logger.LogInformation("{Action} received. SlotId={SlotId}, Peer={Peer}", action, slotId, context.Peer);

            return Task.FromResult(new EFEMReply
            {
                MessageCode = 0,
                Message = $"{action} OK: slotId={slotId}"
            });
        }
    }
}
