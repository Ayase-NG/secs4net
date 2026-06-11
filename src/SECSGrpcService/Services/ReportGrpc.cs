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
    private readonly VidMap _vidMap;
    private readonly CeidMap _ceidMap;
    private readonly IActiveSxFyDispatcher _activeSxFyDispatcher;
    private readonly IDevice _device;
    private readonly IEventLinkStorage _eventLinkStorage;
    private readonly IAlarmEnableStorage _alarmEnableStorage;
    private readonly IAlarmStateStorage _alarmStateStorage;
    private readonly IPortContextStorage _portContextStorage;
    private readonly IJobPlanStorage _jobPlanStorage;
    private readonly IMeasurementDispatcher _measurementDispatcher;

    public ReportGrpc(
        ILogger<ReportGrpc> logger,
        SecsGemContext secsGemContext,
        AlarmStore alarmStore,
        VidMap vidMap,
        CeidMap ceidMap,
        IActiveSxFyDispatcher activeSxFyDispatcher,
        IDevice device,
        IEventLinkStorage eventLinkStorage,
        IAlarmEnableStorage alarmEnableStorage,
        IAlarmStateStorage alarmStateStorage,
        IPortContextStorage portContextStorage,
        IJobPlanStorage jobPlanStorage,
        IMeasurementDispatcher measurementDispatcher)
    {
        _logger = logger;
        _secsGemContext = secsGemContext;
        _alarmStore = alarmStore;
        _vidMap = vidMap;
        _ceidMap = ceidMap;
        _activeSxFyDispatcher = activeSxFyDispatcher;
        _device = device;
        _eventLinkStorage = eventLinkStorage;
        _alarmEnableStorage = alarmEnableStorage;
        _alarmStateStorage = alarmStateStorage;
        _portContextStorage = portContextStorage;
        _jobPlanStorage = jobPlanStorage;
        _measurementDispatcher = measurementDispatcher;
    }

    /// <summary>
    /// 上报 RFID 与 Mapping 结果，S6F11。《暂时不使用！目前用GenericEventReportRequest替代》
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

        // 方法关键节点：Mapping 阶段写入 Port 维度上下文，供后续结果上报兜底使用。
        _portContextStorage.Upsert(new PortRuntimeContext
        {
            PortId = request.PortId,
            CarrierId = request.RFID,
            LotId = request.LotId,
            SlotsList = slotsText
        });

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

        // 方法关键节点：Carrier 到达（Mapping 上报）后按 S14/S16 计划尝试自动调度。
        await TryDispatchPlannedJobOnCarrierArrivedAsync(request, context.CancellationToken).ConfigureAwait(false);

        return new ReportReply
        {
            MessageCode = 0,
            Message = string.Empty
        };
    }

    /// <summary>
    /// 上报花篮取走事件（只传 PORTID），中间件按 Port 上下文补齐 LOTID/RFID/SLOTSLIST 后发送 S6F11。
    /// </summary>
    public override async Task<ReportReply> ReportCarrierRemoved(PortEventMessage request, ServerCallContext context)
    {
        var portId = (request.PortId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(portId))
        {
            return new ReportReply
            {
                MessageCode = 1,
                Message = "PORTID 不能为空。"
            };
        }

        if (!_portContextStorage.TryGetByPortId(portId, out var portContext))
        {
            return new ReportReply
            {
                MessageCode = 2,
                Message = $"未找到 PORTID={portId} 的运行态上下文。"
            };
        }

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PORTID"] = portContext.PortId ?? string.Empty,
            ["LOTID"] = portContext.LotId ?? string.Empty,
            ["RFID"] = portContext.CarrierId ?? string.Empty,
            ["SLOTSLIST"] = portContext.SlotsList ?? string.Empty
        };

        if (!_ceidMap.TryGetCeid("CarrierRemoved", out var ceid))
        {
            return new ReportReply
            {
                MessageCode = 3,
                Message = "CEID.csv 未配置事件名 CarrierRemoved。"
            };
        }

        var linkedRptIds = _eventLinkStorage.GetRptIdsForCeid(ceid);
        if (linkedRptIds is null || linkedRptIds.Count == 0)
        {
            return new ReportReply
            {
                MessageCode = 4,
                Message = $"CEID {ceid} 未绑定 RPTID，请先下发 S2F35。"
            };
        }

        // 方法关键节点：按 Host 运行态绑定的首个 RPTID 生成 CarrierRemoved 事件并发送 S6F11。
        var rptId = linkedRptIds[0];
        var data = ActiveReportSxFyFunctions.BuildGenericEventReport(
            _secsGemContext.GetNextDataId(),
            ceid,
            rptId,
            parameters,
            TryGetVid);

        var sent = await _activeSxFyDispatcher.SendS6F11Async(data, context.CancellationToken).ConfigureAwait(false);
        if (sent)
        {
            _logger.LogInformation("S6F11 sent for CarrierRemoved. CEID={CEID}, RPTID={RPTID}, PortId={PortId}, LotId={LotId}, RFID={RFID}", ceid, rptId, parameters["PORTID"], parameters["LOTID"], parameters["RFID"]);
        }
        else
        {
            _logger.LogWarning("Failed or skipped S6F11 send after CarrierRemoved. PortId={PortId}", portId);
        }

        return new ReportReply
        {
            MessageCode = sent ? 0 : 5,
            Message = sent ? string.Empty : "CarrierRemoved 上报失败或被门禁拦截。"
        };
    }

    /// <summary>
    /// 通用事件上报：EFEM 传 EventName + 参数，本项目通过 CEID.csv 映射到 CEID 并发送 S6F11。
    /// </summary>
    public override async Task<ReportReply> ReportEvent(GenericEventReportRequest request, ServerCallContext context)
    {
        if (!_ceidMap.TryGetCeid(request.EventName, out var ceid))
        {
            return new ReportReply
            {
                MessageCode = 1,
                Message = $"未知事件名：{request.EventName}"
            };
        }

        var linkedRptIds = _eventLinkStorage.GetRptIdsForCeid(ceid);
        if (linkedRptIds is null || linkedRptIds.Count == 0)
        {
            return new ReportReply
            {
                MessageCode = 2,
                Message = $"CEID {ceid} 未绑定 RPTID，请先下发 S2F35。"
            };
        }

        // 方法关键节点：最小实现取首个绑定 RPTID 作为通用事件上报 RPTID。
        var rptId = linkedRptIds[0];
        var data = ActiveReportSxFyFunctions.BuildGenericEventReport(
            _secsGemContext.GetNextDataId(),
            ceid,
            rptId,
            request.Parameters,
            TryGetVid);

        var sent = await _activeSxFyDispatcher.SendS6F11Async(data, context.CancellationToken).ConfigureAwait(false);
        return new ReportReply
        {
            MessageCode = sent ? 0 : 3,
            Message = sent ? string.Empty : $"事件 {request.EventName} 上报失败或被门禁拦截。"
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

        // 方法关键节点：优先使用请求参数，其次按 Port/Lot 上下文兜底。
        var resolvedPortId = request.PortId;
        var resolvedLotId = request.LotId;

        if (!string.IsNullOrWhiteSpace(resolvedPortId) && _portContextStorage.TryGetByPortId(resolvedPortId, out var byPort))
        {
            resolvedLotId = string.IsNullOrWhiteSpace(resolvedLotId) ? byPort.LotId : resolvedLotId;
        }
        else if (string.IsNullOrWhiteSpace(resolvedPortId)
            && !string.IsNullOrWhiteSpace(resolvedLotId)
            && _portContextStorage.TryGetByLotId(resolvedLotId, out var byLot))
        {
            resolvedPortId = byLot.PortId;
        }

        // 方法关键节点：通过主动外发 SxFyFunctions 统一完成 S6F11 打包。
        var data = ActiveReportSxFyFunctions.BuildResultReport(
            _secsGemContext.GetNextDataId(),
            request.WaferId,
            resolvedLotId,
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

        // 方法关键节点：维护当前激活报警状态，供 S5F5/S5F6 查询链路使用。
        _alarmStateStorage.UpsertAlarm(
            request.AlarmId,
            request.AlarmCode is { Length: > 0 } ? request.AlarmCode.Span[0] : (byte)0x80,
            request.AlarmText ?? string.Empty);

        _logger.LogInformation(
            "ReportAlarm received. Source={Source}, AlarmId={AlarmId}, AlarmCode={AlarmCode}, Severity={Severity}, Peer={Peer}",
            request.Source,
            request.AlarmId,
            request.AlarmCode,
            request.Severity,
            context.Peer);

        Console.WriteLine($"Time：{request.OccurredAtUnixMs}进入了报警上报 ReportAlarm，source:{request.Source}, alarmId:{request.AlarmId}, alarmCode:{request.AlarmCode}");

        // if 关键分支：Host 配置禁用该报警 ID 时不发送 S5F1，仅保留本地追溯。
        if (!_alarmEnableStorage.IsAlarmEnabled(request.AlarmId))
        {
            _logger.LogInformation("Alarm send disabled by host config. ALID={ALID}", request.AlarmId);
            return new AlarmReply
            {
                MessageCode = 0,
                Message = "OK",
                RequestId = Guid.NewGuid().ToString("N")
            };
        }

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
    /// 上报整盒晶圆检测完成，S6F11。
    /// </summary>
    public override async Task<ReportReply> ReportLotCompleted(LotCompletedMessage request, ServerCallContext context)
    {
        _logger.LogInformation(
            "ReportLotCompleted received. PortId={PortId}, LotId={LotId}, Status={Status}, Peer={Peer}",
            request.PortId,
            request.LotId,
            request.Status,
            context.Peer);

        var data = ActiveReportSxFyFunctions.BuildLotCompletedReport(
            _secsGemContext.GetNextDataId(),
            request.PortId,
            request.LotId,
            request.Status,
            TryGetVid);

        // if 关键分支：整盒完成后清理对应 Port 上下文，防止后续串批次。
        if (!string.IsNullOrWhiteSpace(request.PortId))
        {
            _portContextStorage.RemoveByPortId(request.PortId);
        }

        var sent = await _activeSxFyDispatcher.SendS6F11Async(data, context.CancellationToken).ConfigureAwait(false);
        if (sent)
        {
            _logger.LogInformation("S6F11 sent for ReportLotCompleted. CEID={CEID}, RPTID={RPTID}, PortId={PortId}, LotId={LotId}", data.CEID, data.Reports[0].RPTID, request.PortId, request.LotId);
        }
        else
        {
            _logger.LogWarning("Failed or skipped S6F11 send after ReportLotCompleted. PortId={PortId}, LotId={LotId}", request.PortId, request.LotId);
        }

        return new ReportReply
        {
            MessageCode = 0,
            Message = string.Empty
        };
    }

    /// <summary>
    /// 请求切换远程在线状态。
    /// 规则：仅当当前为 OnLineLocal 时切换为 OnLineRemote 并返回成功。
    /// </summary>
    public override async Task<OnlineStatusReply> RequestOnlineStatus(OnlineStatusRequest request, ServerCallContext context)
    {
        // if 关键分支：当前处于 OnLineLocal 时允许切换为 OnLineRemote。本地转远程
        if (_device.IsOnline == DeviceOnlineState.OnLineLocal && request.Source == "remote")
        {
            var fromState = _device.IsOnline.ToString();
            _device.IsOnline = DeviceOnlineState.OnLineRemote;
            var toState = _device.IsOnline.ToString();

            // 方法关键节点：状态切换成功后，按预设模板发送 OnlineStateChanged 的 S6F11（CEID=1021）。
            var stateChangedData = ActiveReportSxFyFunctions.BuildOnlineStateChangedReport(
                _secsGemContext.GetNextDataId(),
                fromState,
                toState,
                "RequestOnlineStatus",
                TryGetVid);

            var sent = await _activeSxFyDispatcher.SendS6F11Async(stateChangedData, context.CancellationToken).ConfigureAwait(false);
            if (sent)
            {
                _logger.LogInformation("Online state changed reported. CEID={CEID}, From={FromState}, To={ToState}", stateChangedData.CEID, fromState, toState);
            }
            else
            {
                _logger.LogWarning("Online state changed report skipped/failed. CEID={CEID}, From={FromState}, To={ToState}", stateChangedData.CEID, fromState, toState);
            }

            return new OnlineStatusReply
            {
                MessageCode = 0,
                Message = "切换成功，已进入远程在线状态。",
                Online = true
            };
        // 远程转本地
        }else if (_device.IsOnline == DeviceOnlineState.OnLineRemote && request.Source == "local")
        {
            var fromState = _device.IsOnline.ToString();
            _device.IsOnline = DeviceOnlineState.OnLineLocal;
            var toState = _device.IsOnline.ToString();

            // 方法关键节点：状态切换成功后，按预设模板发送 OnlineStateChanged 的 S6F11（CEID=1021）。
            var stateChangedData = ActiveReportSxFyFunctions.BuildOnlineStateChangedReport(
                _secsGemContext.GetNextDataId(),
                fromState,
                toState,
                "RequestOnlineStatus",
                TryGetVid);

            var sent = await _activeSxFyDispatcher.SendS6F11Async(stateChangedData, context.CancellationToken).ConfigureAwait(false);
            if (sent)
            {
                _logger.LogInformation("Online state changed reported. CEID={CEID}, From={FromState}, To={ToState}", stateChangedData.CEID, fromState, toState);
            }
            else
            {
                _logger.LogWarning("Online state changed report skipped/failed. CEID={CEID}, From={FromState}, To={ToState}", stateChangedData.CEID, fromState, toState);
            }
            return new OnlineStatusReply
            {
                MessageCode = 0,
                Message = "切换成功，已进入本地在线状态。",
                Online = true
            };
        }
        else if (_device.IsOnline == DeviceOnlineState.OnLineLocal && request.Source == "local")
        {
            // if 关键分支：当前为 OffLine 时拒绝切换并提示。
            return new OnlineStatusReply
            {
                MessageCode = 1,
                Message = "目前为本地在线状态，无需切换。",
                Online = true
            };
        }
        else if (_device.IsOnline == DeviceOnlineState.OnLineRemote && request.Source == "remote")
        {
            // if 关键分支：当前为 OffLine 时允许切换为 OnLineLocal。
            return new OnlineStatusReply
            {
                MessageCode = 2,
                Message = "当前已处于远程在线状态，无需切换。",
                Online = true
            };
        }

        // 兜底分支：非 OnLineLocal 场景拒绝切换。
        return new OnlineStatusReply
        {
            MessageCode = 1,
            Message = "目前为离线状态，禁止切换为远程。",
            Online = false
        };
    }

    /// <summary>
    /// VID 映射委托，供主动外发打包函数注入。
    /// </summary>
    private (bool Found, ushort Vid) TryGetVid(string cpName)
    {
        // if 关键分支：映射命中则返回对应 VID。
        if (_vidMap.TryGetVid(cpName, out var vid))
        {
            return (true, vid);
        }

        // 兜底分支：未命中映射时返回 false 与 0。
        return (false, 0);
    }

    /// <summary>
    /// 将报警代码字节转换为可读字符串。
    /// 作用：优先按 UTF8 解码，失败时回退十六进制，便于日志与上报文本统一展示。
    /// </summary>
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

    /// <summary>
    /// Carrier 到达后的计划调度入口。
    /// 作用：基于已缓存的 ControlJob/ProcessJob 与当前 Carrier 上下文，按 PRPROCESSSTART 决定自动下发 PPSELECT+START 或等待 Host 指令。
    /// </summary>
    private async Task TryDispatchPlannedJobOnCarrierArrivedAsync(CarrierMessage request, CancellationToken cancellationToken)
    {
        var carrierId = (request.RFID ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(carrierId))
        {
            return;
        }

        var controlJobs = _jobPlanStorage.GetAllControlJobs();
        if (controlJobs.Count == 0)
        {
            return;
        }

        foreach (var cj in controlJobs.OrderByDescending(x => x.UpdatedAtUtc))
        {
            // if 关键分支：当前到达 Carrier 不在 CJ 输入列表时跳过。
            if (cj.CarrierInputSpec.Count == 0
                || !cj.CarrierInputSpec.Any(x => string.Equals(x, carrierId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            foreach (var pjId in cj.ProcessingCtrlSpec)
            {
                if (!_jobPlanStorage.TryGetProcessJob(pjId, out var pj))
                {
                    continue;
                }

                // if 关键分支：PRPROCESSSTART=false 时仅保留计划等待 Host 显式 START。
                if (!pj.AutoStart)
                {
                    _logger.LogInformation("ProcessJob waits host command because PRPROCESSSTART=false. PJID={PJID}, CarrierId={CarrierId}", pj.PJID, carrierId);
                    continue;
                }

                // if 关键分支：已自动触发过则跳过，避免重复启动。
                if (pj.AutoStarted)
                {
                    continue;
                }

                var matchedCarrier = pj.Carriers.FirstOrDefault(x => string.Equals(x.CarrierId, carrierId, StringComparison.OrdinalIgnoreCase));
                if (matchedCarrier is null)
                {
                    continue;
                }

                var slots = matchedCarrier.Slots.Count > 0
                    ? matchedCarrier.Slots.Where(x => x > 0).Distinct().ToList()
                    : request.SlotsList.Where(x => x > 0).Distinct().ToList();

                var ppSelect = new S2F41_data
                {
                    RCMD = "PPSELECT",
                    Parameters = new Dictionary<string, Secs4Net.Item>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["PPID"] = Secs4Net.Item.A(string.IsNullOrWhiteSpace(pj.RecipeId) ? "DEFAULT" : pj.RecipeId),
                        ["LOTID"] = Secs4Net.Item.A(request.LotId ?? string.Empty),
                        ["PORTID"] = Secs4Net.Item.A(request.PortId ?? string.Empty),
                        ["MODE"] = Secs4Net.Item.A(_device.Mode)
                    }
                };

                if (slots.Count > 0)
                {
                    ppSelect.Parameters["SLOTSLIST"] = Secs4Net.Item.A(string.Join(',', slots));
                }

                // 方法关键节点：异步下发 PPSELECT，先把配方/批次/端口/槽位上下文写入设备侧，作为 START 前置条件。
                await _measurementDispatcher.DispatchProcessProgramSelectAsync(ppSelect, cancellationToken).ConfigureAwait(false);

                var start = new S2F41_data
                {
                    RCMD = "START",
                    Parameters = new Dictionary<string, Secs4Net.Item>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["LOTID"] = Secs4Net.Item.A(request.LotId ?? string.Empty),
                        ["PORTID"] = Secs4Net.Item.A(request.PortId ?? string.Empty)
                    }
                };

                // 方法关键节点：在 PPSELECT 成功后异步下发 START，驱动设备执行对应 ProcessJob。
                await _measurementDispatcher.DispatchStartMeasurementAsync(start, cancellationToken).ConfigureAwait(false);

                _jobPlanStorage.MarkProcessJobAutoStarted(pj.PJID, request.PortId ?? string.Empty, request.LotId ?? string.Empty);
                _logger.LogInformation("Auto dispatched planned job on carrier arrived. CJID={CJID}, PJID={PJID}, CarrierId={CarrierId}, PortId={PortId}, LotId={LotId}", cj.CJID, pj.PJID, carrierId, request.PortId, request.LotId);
                return;
            }
        }
    }
}
