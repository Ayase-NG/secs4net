using Grpc.Core;

namespace SECSGrpcService.Services;

/// <summary>
/// 上报相关 gRPC 服务实现，对应 <c>efem.proto</c>。
/// </summary>
public sealed class ReportGrpc : global::SECSGrpcService.ReportGrpcService.ReportGrpcServiceBase
{
    private readonly ILogger<ReportGrpc> _logger;

    public ReportGrpc(ILogger<ReportGrpc> logger)
    {
        _logger = logger;
    }

    public override Task<ReportReply> ResultReport(WaferMessage request, ServerCallContext context)
    {
        _logger.LogInformation(
            "ResultReport received. SlotId={SlotId}, WaferId={WaferId}, LotId={LotId}, Status={Status}, PPID={PPID}, Result={Result}, Peer={Peer}",
            request.SlotId,
            request.WaferId,
            request.LotId,
            request.Status,
            request.PPID,
            request.Result,
            context.Peer);

        Console.WriteLine($"进入了检测结果上报 ResultReport，waferId:{request.WaferId}, PPID:{request.PPID}, slotId:{request.SlotId}");

        return Task.FromResult(new ReportReply
        {
            MessageCode = 0,
            Message = string.Empty
        });
    }

    public override Task<AlarmReply> ReportAlarm(AlarmReportRequest request, ServerCallContext context)
    {
        AlarmStore.Upsert(request);

        _logger.LogInformation(
            "ReportAlarm received. Source={Source}, AlarmId={AlarmId}, AlarmCode={AlarmCode}, Severity={Severity}, Peer={Peer}",
            request.Source,
            request.AlarmId,
            request.AlarmCode,
            request.Severity,
            context.Peer);

        Console.WriteLine($"进入了报警上报 ReportAlarm，source:{request.Source}, alarmId:{request.AlarmId}, alarmCode:{request.AlarmCode}");

        return Task.FromResult(new AlarmReply
        {
            MessageCode = 0,
            Message = "OK",
            RequestId = Guid.NewGuid().ToString("N")
        });
    }
}
