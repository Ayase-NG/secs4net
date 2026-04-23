using Grpc.Core;
using SECSbuilder;
using SECSdata;

namespace SECSGrpcService.Services;

/// <summary>
/// 上报相关 gRPC 服务实现，对应 <c>efem.proto</c>。
/// </summary>
public sealed class ReportGrpc : global::SECSGrpcService.ReportGrpcService.ReportGrpcServiceBase
{
    private readonly ILogger<ReportGrpc> _logger;
    private readonly SecsGemContext _secsGemContext;

    public ReportGrpc(ILogger<ReportGrpc> logger, SecsGemContext secsGemContext)
    {
        _logger = logger;
        _secsGemContext = secsGemContext;
    }

    public override async Task<ReportReply> ResultReport(WaferMessage request, ServerCallContext context)
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

        Console.WriteLine($"进入了检测结果上报 ResultReport，waferId:{request.WaferId}, PPID:{request.PPID}, slotId:{request.SlotId}，result：{request.Result}");

        if (_secsGemContext.TryGet(out var secsGem) && secsGem is not null)
        {
            var data = new S6F11_data
            {
                DATAID = 0,
                CEID = 1009,
                Reports = new List<S6F11_report_data>
                {
                    new S6F11_report_data
                    {
                        RPTID = 1009,
                        Values = new List<object?>
                        {
                            request.WaferId,
                            request.LotId,
                            request.PPID,
                            request.SlotId,
                            request.Status,
                            request.Result
                        }
                    }
                }
            };

            try
            {
                var s6f11 = S6F11_builder.Build(data);
                await secsGem.SendAsync(s6f11, context.CancellationToken).ConfigureAwait(false);
                _logger.LogInformation("S6F11 sent. CEID={CEID}, RPTID={RPTID}, WaferId={WaferId}, Result={Result}", data.CEID, data.Reports[0].RPTID, request.WaferId, request.Result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send S6F11 after ResultReport.");
            }
        }
        else
        {
            _logger.LogWarning("SECS session not available. Skip S6F11 send after ResultReport.");
        }

        return new ReportReply
        {
            MessageCode = 0,
            Message = string.Empty
        };
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
