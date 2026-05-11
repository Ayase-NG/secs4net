using Google.Protobuf;
using Grpc.Core;
using SECSbuilder;
using SECSdata;
using SECShandler.Functions;

namespace SECSGrpcService.Services;

/// <summary>
/// 上报相关 gRPC 服务实现，对应 <c>efem.proto</c>。
/// </summary>
public sealed class ReportGrpc : GY.SECS.ReportGrpcService.ReportGrpcServiceBase
{
    private readonly ILogger<ReportGrpc> _logger;
    private readonly SecsGemContext _secsGemContext;
    private readonly AlarmStore _alarmStore;
    private readonly CommandParameterMap _commandParameterMap;

    public ReportGrpc(
        ILogger<ReportGrpc> logger,
        SecsGemContext secsGemContext,
        AlarmStore alarmStore,
        CommandParameterMap commandParameterMap)
    {
        _logger = logger;
        _secsGemContext = secsGemContext;
        _alarmStore = alarmStore;
        _commandParameterMap = commandParameterMap;
    }

    /// <summary>
    /// 上报 RFID 与 Mapping 结果，S6F11。
    /// </summary>
    public override async Task<ReportReply> ReportRFID(CarrierMessage request, ServerCallContext context)
    {
        var slotsText = request.SlotsList.Count > 0 ? string.Join(',', request.SlotsList) : string.Empty;

        _logger.LogInformation(
            "ReportRFID received. PortId={PortId}, LotId={LotId}, RFID={RFID}, SlotsList={SlotsList}, Peer={Peer}",
            request.PortId,
            request.LotId,
            request.RFID,
            slotsText,
            context.Peer);

        Console.WriteLine($"进入RFID上报 ReportRFID，portId:{request.PortId}, lotId:{request.LotId}, RFID:{request.RFID}, slotsList:{slotsText}");

        // 关键分支：存在活动 SECS 会话时发送 S6F11，否则仅记录警告并返回。
        if (_secsGemContext.TryGet(out var secsGem) && secsGem is not null)
        {
            var data = new S6F11_data
            {
                DATAID = _secsGemContext.GetNextDataId(),
                CEID = 1007,
                Reports = new List<S6F11_report_data>
                {
                    new S6F11_report_data
                    {
                        RPTID = 1002,
                        // Mapping结果预设模板
                        Values = new List<S6F11_parameter_data>
                        {
                            CreateParam("PORTID", request.PortId),
                            CreateParam("LOTID", request.LotId),
                            CreateParam("RFID", request.RFID),
                            CreateParam("SLOTSLIST", slotsText)
                        }
                    }
                }
            };

            try
            {
                var s6f11 = S6F11_builder.Build(data);
                await secsGem.SendAsync(s6f11, context.CancellationToken).ConfigureAwait(false);
                _logger.LogInformation("S6F11 sent for ReportRFID. CEID={CEID}, RPTID={RPTID}, PortId={PortId}, RFID={RFID}", data.CEID, data.Reports[0].RPTID, request.PortId, request.RFID);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send S6F11 after ReportRFID.");
            }
        }
        else
        {
            _logger.LogWarning("SECS session not available. Skip S6F11 send after ReportRFID.");
        }

        return new ReportReply
        {
            MessageCode = 0,
            Message = string.Empty
        };
    }

    /// <summary>
    /// 上报晶圆测试结果，S6F11。
    /// </summary>
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

        // 关键分支：存在活动 SECS 会话时发送 S6F11，否则仅记录警告并返回。
        if (_secsGemContext.TryGet(out var secsGem) && secsGem is not null)
        {
            var data = new S6F11_data
            {
                DATAID = _secsGemContext.GetNextDataId(),
                CEID = 1001,
                Reports = new List<S6F11_report_data>
                {
                    new S6F11_report_data
                    {
                        RPTID = 1000,
                        Values = new List<S6F11_parameter_data>
                        {
                            CreateParam("WAFERID", request.WaferId),
                            CreateParam("LOTID", request.LotId),
                            CreateParam("PPID", request.PPID),
                            CreateParam("SLOTID", request.SlotId),
                            CreateParam("RESULT", request.Result)
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

    public override async Task<AlarmReply> ReportAlarm(AlarmReportRequest request, ServerCallContext context)
    {
        _alarmStore.Upsert(request);

        _logger.LogInformation(
            "ReportAlarm received. Source={Source}, AlarmId={AlarmId}, AlarmCode={AlarmCode}, Severity={Severity}, Peer={Peer}",
            request.Source,
            request.AlarmId,
            request.AlarmCode,
            request.Severity,
            context.Peer);

        Console.WriteLine($"Time：{request.OccurredAtUnixMs}进入了报警上报 ReportAlarm，source:{request.Source}, alarmId:{request.AlarmId}, alarmCode:{request.AlarmCode}");

        // 关键分支：存在活动 SECS 会话时发送 S5F1，否则仅记录警告并返回。
        if (_secsGemContext.TryGet(out var secsGem) && secsGem is not null)
        {
            var alarmCodeText = ToAlarmCodeString(request.AlarmCode);
            var s5f1Data = new S5F1_data
            {
                ALCD = request.AlarmCode is { Length: > 0 } ? request.AlarmCode.Span[0] : (byte)0x80,
                ALID = request.AlarmId,
                ALTX = string.IsNullOrWhiteSpace(request.AlarmText)
                    ? (string.IsNullOrWhiteSpace(alarmCodeText) ? "报警已解除" : alarmCodeText)
                    : $"{alarmCodeText}:{request.AlarmText}"
            };

            await EventReportSxFyFunctions.SendS5F1WithRetryAsync(secsGem, s5f1Data, _logger, context.CancellationToken).ConfigureAwait(false);
        }
        else
        {
            _logger.LogWarning("SECS session not available. Skip S5F1 send after ReportAlarm.");
        }

        return new AlarmReply
        {
            MessageCode = 0,
            Message = "OK",
            RequestId = Guid.NewGuid().ToString("N")
        };
    }

    /// <summary>
    /// 构建 S6F11 参数项：VID/CPName/CPVal。
    /// 实际上传结构由 builder 编码为 VID+CPVal。
    /// </summary>
    private S6F11_parameter_data CreateParam(string cpName, object? value)
    {
        // 关键分支：映射命中则填写 VID，未命中则使用 0 作为未知 VID。
        var hasVid = _commandParameterMap.TryGetVid(cpName, out var vid);

        return new S6F11_parameter_data
        {
            VID = hasVid ? vid : (ushort)0,
            CPName = cpName,
            CPVal = value?.ToString() ?? string.Empty
        };
    }

    private static string ToAlarmCodeString(ByteString alarmCode)
    {
        if (alarmCode is null || alarmCode.Length == 0)
            return string.Empty;

        try
        {
            var utf8 = alarmCode.ToStringUtf8();
            if (!string.IsNullOrWhiteSpace(utf8))
                return utf8;
        }
        catch
        {
        }

        return Convert.ToHexString(alarmCode.ToByteArray());
    }
}
