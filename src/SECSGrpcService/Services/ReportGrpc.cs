using Google.Protobuf;
using Grpc.Core;
using SECSdata;
using SECShandler.Functions;
using SECShandler.Interfaces;

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
    private readonly IActiveSxFyDispatcher _activeSxFyDispatcher;

    public ReportGrpc(
        ILogger<ReportGrpc> logger,
        SecsGemContext secsGemContext,
        AlarmStore alarmStore,
        CommandParameterMap commandParameterMap,
        IActiveSxFyDispatcher activeSxFyDispatcher)
    {
        _logger = logger;
        _secsGemContext = secsGemContext;
        _alarmStore = alarmStore;
        _commandParameterMap = commandParameterMap;
        _activeSxFyDispatcher = activeSxFyDispatcher;
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

        // 方法关键节点：通过主动外发 SxFyFunctions 统一完成 S6F11 打包。
        var data = ActiveReportSxFyFunctions.BuildRfidReport(
            _secsGemContext.GetNextDataId(),
            request.PortId,
            request.LotId,
            request.RFID,
            slotsText,
            TryGetVid);

        // if 关键分支：发送成功与失败分别记录日志，不影响 gRPC 返回。
        var sent = await _activeSxFyDispatcher.SendS6F11Async(data, context.CancellationToken).ConfigureAwait(false);
        if (sent)
        {
            _logger.LogInformation("S6F11 sent for ReportRFID. CEID={CEID}, RPTID={RPTID}, PortId={PortId}, RFID={RFID}", data.CEID, data.Reports[0].RPTID, request.PortId, request.RFID);
        }
        else
        {
            _logger.LogWarning("Failed or skipped S6F11 send after ReportRFID. PortId={PortId}, RFID={RFID}", request.PortId, request.RFID);
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

        // 方法关键节点：通过主动外发 SxFyFunctions 统一完成 S6F11 打包。
        var data = ActiveReportSxFyFunctions.BuildResultReport(
            _secsGemContext.GetNextDataId(),
            request.WaferId,
            request.LotId,
            request.PPID,
            request.SlotId,
            request.Result,
            TryGetVid);

        // if 关键分支：发送成功与失败分别记录日志，不影响 gRPC 返回。
        var sent = await _activeSxFyDispatcher.SendS6F11Async(data, context.CancellationToken).ConfigureAwait(false);
        if (sent)
        {
            _logger.LogInformation("S6F11 sent. CEID={CEID}, RPTID={RPTID}, WaferId={WaferId}, Result={Result}", data.CEID, data.Reports[0].RPTID, request.WaferId, request.Result);
        }
        else
        {
            _logger.LogWarning("Failed or skipped S6F11 send after ResultReport. WaferId={WaferId}, Result={Result}", request.WaferId, request.Result);
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
    /// VID 映射委托，供主动外发打包函数注入。
    /// </summary>
    private (bool Found, ushort Vid) TryGetVid(string cpName)
    {
        // if 关键分支：映射命中则返回对应 VID。
        if (_commandParameterMap.TryGetVid(cpName, out var vid))
        {
            return (true, vid);
        }

        // 兜底分支：未命中映射时返回 false 与 0。
        return (false, 0);
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
