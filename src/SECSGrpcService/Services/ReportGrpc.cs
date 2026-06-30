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
    /// 上报 RFID 与 Mapping 结果。
    /// 作用：兼容旧接口，将请求转换为 CEID=1002 的 ReportEvent 统一处理链路。
    /// </summary>
    public override async Task<ReportReply> ReportRFID(CarrierMessage request, ServerCallContext context)
    {
        // 打印入站 gRPC 载荷，便于联调时快速核对设备上报字段和值。
        var inboundSlots = request.SlotsList.Count > 0 ? string.Join(',', request.SlotsList) : "<empty>";
        _logger.LogInformation(
            "gRPC IN ReportRFID. Peer={Peer}, CARRIERID={CarrierId}, LOTID={LotId}, PORTID={PortId}, SLOTSLIST={SlotsList}",
            context.Peer,
            request.RFID,
            request.LotId,
            request.PortId,
            inboundSlots);

        var slotsText = request.SlotsList.Count > 0 ? string.Join(',', request.SlotsList) : string.Empty;

        // 旧 ReportRFID 请求统一转换为 ReportEvent(CEID=1002)。
        var eventRequest = new GenericEventReportRequest
        {
            CEID = 1002
        };
        // 对外统一使用 CARRIERID 作为载具字段名。
        eventRequest.Parameters["CARRIERID"] = request.RFID ?? string.Empty;
        eventRequest.Parameters["LOTID"] = request.LotId ?? string.Empty;
        eventRequest.Parameters["PORTID"] = request.PortId ?? string.Empty;
        eventRequest.Parameters["SLOTSLIST"] = slotsText;

        // 异步复用 ReportEvent 统一链路，避免维护两套 1002 上报逻辑。
        return await ReportEvent(eventRequest, context).ConfigureAwait(false);
    }

    /// <summary>
    /// 上报花篮取走事件（只传 PORTID），中间件按 Port 上下文补齐 LOTID/RFID/SLOTSLIST 后发送 S6F11。
    /// </summary>
    public override async Task<ReportReply> ReportCarrierRemoved(PortEventMessage request, ServerCallContext context)
    {
        // 打印入站 gRPC 载荷，便于联调时确认载具取走事件参数。
        _logger.LogInformation("gRPC IN ReportCarrierRemoved. Peer={Peer}, PORTID={PortId}", context.Peer, request.PortId);

        var portId = (request.PortId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(portId))
        {
            return new ReportReply
            {
                Success = false,
                Code = 1,
                Message = "PORTID 不能为空。"
            };
        }

        if (!_portContextStorage.TryGetByPortId(portId, out var portContext))
        {
            return new ReportReply
            {
                Success = false,
                Code = 2,
                Message = $"未找到 PORTID={portId} 的运行态上下文。"
            };
        }

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PORTID"] = portContext.PortId ?? string.Empty,
            ["LOTID"] = portContext.LotId ?? string.Empty,
            ["CARRIERID"] = portContext.CarrierId ?? string.Empty,
            ["SLOTSLIST"] = portContext.SlotsList ?? string.Empty
        };

        if (!_ceidMap.TryGetCeid("CarrierRemoved", out var ceid))
        {
            return new ReportReply
            {
                Success = false,
                Code = 3,
                Message = "CEID.csv 未配置事件名 CarrierRemoved。"
            };
        }

        var linkedRptIds = _eventLinkStorage.GetRptIdsForCeid(ceid);
        if (linkedRptIds is null || linkedRptIds.Count == 0)
        {
            return new ReportReply
            {
                Success = false,
                Code = 4,
                Message = $"CEID {ceid} 未绑定 RPTID，请先下发 S2F35。"
            };
        }

        // 按 Host 运行态绑定的首个 RPTID 生成 CarrierRemoved 事件并发送 S6F11。
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
            _logger.LogInformation("S6F11 sent for CarrierRemoved. CEID={CEID}, RPTID={RPTID}, PortId={PortId}, LotId={LotId}, CarrierId={CarrierId}", ceid, rptId, parameters["PORTID"], parameters["LOTID"], parameters["CARRIERID"]);
        }
        else
        {
            _logger.LogWarning("Failed or skipped S6F11 send after CarrierRemoved. PortId={PortId}", portId);
        }

        return new ReportReply
        {
            Success = sent,
            Code = sent ? 0 : 5,
            Message = sent ? string.Empty : "CarrierRemoved 上报失败或被门禁拦截。"
        };
    }

    /// <summary>
    /// 通用事件上报：EFEM 传 EventName + 参数，本项目通过 CEID.csv 映射到 CEID 并发送 S6F11。
    /// </summary>
    public override async Task<ReportReply> ReportEvent(GenericEventReportRequest request, ServerCallContext context)
    {
        // 打印入站 gRPC 通用事件参数，便于排查 CEID 对应值是否正确传入。
        _logger.LogInformation(
            "gRPC IN ReportEvent. Peer={Peer}, CEID={CEID}, Params={Params}",
            context.Peer,
            request.CEID,
            ToParameterLogText(request.Parameters));

        // 先标准化事件参数键名，统一为 CARRIERID/SLOTSLIST/PORTID，提升后续映射兼容性。
        //var normalizedParameters = NormalizeEventParameters(request.Parameters);

        // 通用事件上报前先尝试用事件参数增量刷新 Port 上下文，避免后续 PPSELECT 校验读取到旧值。
        TryUpdatePortContextFromEventParameters(request.Parameters);

        // if 关键分支：请求 CEID 未在 CEID.csv 中定义时直接拒绝，避免上报未注册事件。
        if (!_ceidMap.TryGetEventName(request.CEID, out var eventName))
        {
            return new ReportReply
            {
                Success = false,
                Code = 1,
                Message = $"未知事件名：{request.CEID}"
            };
        }

        // 按请求 CEID 查询 Host 运行态绑定的 RPTID，保持 S2F35 动态绑定语义。
        var linkedRptIds = _eventLinkStorage.GetRptIdsForCeid(request.CEID);
        if (linkedRptIds is null || linkedRptIds.Count == 0)
        {
            return new ReportReply
            {
                Success = false,
                Code = 2,
                Message = $"CEID {request.CEID} 未绑定 RPTID，请先下发 S2F35。"
            };
        }

        // if 关键分支：在线状态变化事件走固定模板，字段仅使用 FROM_STATE/TO_STATE/TRIGGER，保持语义稳定。
        if (request.CEID == 1006)
        {
            // 兼容按 VID 键名上传（3001/3002/3004）与按业务键名上传（FROM_STATE/TO_STATE/TRIGGER）。
            var fromState = ReadParameter(request.Parameters, "FROM_STATE", "3001") ?? string.Empty;
            var toState = ReadParameter(request.Parameters, "TO_STATE", "3002") ?? string.Empty;
            var trigger = ReadParameter(request.Parameters, "TRIGGER", "3004") ?? string.Empty;

            // 1006 使用固定模板组包，避免通用参数波动影响状态事件结构。
            var stateChangedData = ActiveReportSxFyFunctions.BuildOnlineStateChangedReport(
                _secsGemContext.GetNextDataId(),
                fromState,
                toState,
                trigger,
                TryGetVid);

            // 异步发送 1006 事件，上报是否成功仍由 S2F33/S2F35/S2F37 门禁决定。
            var stateSent = await _activeSxFyDispatcher.SendS6F11Async(stateChangedData, context.CancellationToken).ConfigureAwait(false);
            return new ReportReply
            {
                Success = stateSent,
                Code = stateSent ? 0 : 3,
                Message = stateSent ? string.Empty : $"事件 {eventName}({request.CEID}) 上报失败或被门禁拦截。"
            };
        }

        // 最小实现取首个绑定 RPTID 作为通用事件上报 RPTID。
        var rptId = linkedRptIds[0];
        var data = ActiveReportSxFyFunctions.BuildGenericEventReport(
            _secsGemContext.GetNextDataId(),
            request.CEID,
            rptId,
            request.Parameters,
            TryGetVid);

        // 当 CEID=1001（WaferResultReported）时，按 S16F15/S14F9 缓存计划仅校验 CarrierId 是否存在。
        var isCarrierPlanned = true;
        if (request.CEID == 1001)
        {
            isCarrierPlanned = TryValidateCarrierInPlannedJobsForResultEvent(request.Parameters);
        }

        // 异步发送通用事件 S6F11，上报是否成功仍受 S2F33/S2F35/S2F37 门禁控制。
        var sent = await _activeSxFyDispatcher.SendS6F11Async(data, context.CancellationToken).ConfigureAwait(false);

        // if 关键分支：仅在成功上报后执行事件后置处理，确保运行态与 Host 已接收事件一致。
        if (sent)
        {
            // 按事件 CEID 处理 Port 上下文生命周期（如 LotCompleted/CarrierRemoved 清理）。
            TryFinalizePortContextByEvent(request.CEID, request.Parameters);

            // CEID=1002 视为 Carrier 到达，按 S14/S16 缓存计划匹配后下发 PPSELECT。
            if (request.CEID == 1002)
            {
                var carrier = BuildCarrierMessageFromEventParameters(request.Parameters);

                // if 关键分支：仅在能解析到有效 CARRIERID 时触发调度，避免误触发。
                if (!string.IsNullOrWhiteSpace(carrier.RFID))
                {
                    // 异步按 S16F15/S14F9 缓存计划仅下发 PPSELECT，并使用任务中的槽位列表。
                    await TryDispatchProcessProgramSelectOnCarrierEventAsync(carrier, context.CancellationToken).ConfigureAwait(false);
                }
            }
        }

        // if 关键分支：CEID=1001 时返回 Carrier 计划校验结果码（10021=存在，10022=不存在）。
        if (request.CEID == 1001)
        {
            return new ReportReply
            {
                Success = sent,
                Code = sent ? (isCarrierPlanned ? 10021 : 10022) : 3,
                Message = sent
                    ? (isCarrierPlanned
                        ? "WaferResultReported 上报成功，CarrierId 已匹配 S16F15/S14F9 作业计划。"
                        : "WaferResultReported 上报成功，但 CarrierId 未匹配到 S16F15/S14F9 作业计划。")
                    : $"事件 {eventName}({request.CEID}) 上报失败或被门禁拦截。"
            };
        }

        return new ReportReply
        {
            Success = sent,
            Code = sent ? 0 : 3,
            Message = sent ? string.Empty : $"事件 {eventName}({request.CEID}) 上报失败或被门禁拦截。"
        };
    }

    /// <summary>
    /// 标准化通用事件参数键名。
    /// 作用：统一外部别名到 CARRIERID/SLOTSLIST/PORTID，避免同义字段造成 VID 映射歧义。
    /// </summary>
    private static Dictionary<string, string> NormalizeEventParameters(IDictionary<string, string> parameters)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // for each 关键分支：先复制原始参数，保留业务侧自定义扩展字段。
        foreach (var kv in parameters)
        {
            if (!string.IsNullOrWhiteSpace(kv.Key))
            {
                normalized[kv.Key.Trim()] = kv.Value ?? string.Empty;
            }
        }

        // 统一标准字段，优先保留标准键，缺失时从别名提升。
        PromoteCanonicalParameter(normalized, "CARRIERID", "RFID");
        PromoteCanonicalParameter(normalized, "SLOTSLIST", "SLOTS", "slotsList");
        PromoteCanonicalParameter(normalized, "PORTID", "PORT", "LOADPORT");

        // 移除已归一化的别名，避免同义参数重复进入 S6F11。
        normalized.Remove("RFID");
        normalized.Remove("SLOTS");
        normalized.Remove("slotsList");
        normalized.Remove("PORT");
        normalized.Remove("LOADPORT");

        return normalized;
    }

    /// <summary>
    /// 将候选别名提升为标准参数键。
    /// 作用：确保后续参数读取和 VID 映射始终使用统一键名。
    /// </summary>
    private static void PromoteCanonicalParameter(Dictionary<string, string> parameters, string canonicalKey, params string[] aliases)
    {
        if (string.IsNullOrWhiteSpace(canonicalKey))
        {
            return;
        }

        // if 关键分支：标准键已存在且有值时直接保留，不覆盖。
        if (parameters.TryGetValue(canonicalKey, out var existingCanonical) && !string.IsNullOrWhiteSpace(existingCanonical))
        {
            return;
        }

        foreach (var alias in aliases)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                continue;
            }

            if (parameters.TryGetValue(alias, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                parameters[canonicalKey] = value;
                return;
            }
        }
    }

    /// <summary>
    /// 将通用事件参数转换为 CarrierMessage。
    /// 作用：在 CEID=1002 场景复用既有 Carrier 到达自动调度逻辑。
    /// </summary>
    private static CarrierMessage BuildCarrierMessageFromEventParameters(IDictionary<string, string> parameters)
    {
        var message = new CarrierMessage
        {
            PortId = ReadParameter(parameters, "PORTID", "PORT", "LOADPORT")?.Trim() ?? string.Empty,
            LotId = ReadParameter(parameters, "LOTID")?.Trim() ?? string.Empty,
            RFID = ReadParameter(parameters, "CARRIERID", "RFID")?.Trim() ?? string.Empty
        };

        var slotsText = ReadParameter(parameters, "SLOTSLIST", "SLOTS")?.Trim();
        foreach (var slot in ParseSlotsList(slotsText))
        {
            message.SlotsList.Add(slot);
        }

        return message;
    }

    /// <summary>
    /// 解析逗号分隔的槽位字符串。
    /// 作用：将事件参数中的 SLOTSLIST 文本安全转换为槽位整数列表。
    /// </summary>
    private static IReadOnlyList<uint> ParseSlotsList(string? slotsText)
    {
        if (string.IsNullOrWhiteSpace(slotsText))
        {
            return Array.Empty<uint>();
        }

        var result = new List<uint>();
        foreach (var token in slotsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (uint.TryParse(token, out var slot) && slot > 0)
            {
                result.Add(slot);
            }
        }

        return result;
    }

    /// <summary>
    /// Carrier 事件触发的 PPSELECT 调度入口。
    /// 作用：当 CEID=1002 命中 S14F9/S16F15 缓存计划时，仅下发 ProcessProgramSelect，且槽位使用任务内定义。
    /// </summary>
    private async Task TryDispatchProcessProgramSelectOnCarrierEventAsync(CarrierMessage request, CancellationToken cancellationToken)
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
            // if 关键分支：当前 Carrier 不在 CJ 输入列表时跳过。
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

                var matchedCarrier = pj.Carriers.FirstOrDefault(x => string.Equals(x.CarrierId, carrierId, StringComparison.OrdinalIgnoreCase));
                if (matchedCarrier is null)
                {
                    continue;
                }

                var slots = matchedCarrier.Slots.Where(x => x > 0).Distinct().ToList();
                // if 关键分支：任务内未定义槽位时不下发 PPSELECT，避免使用非任务槽位。
                if (slots.Count == 0)
                {
                    continue;
                }

                var ppSelect = new S2F41_data
                {
                    RCMD = "PPSELECT",
                    Parameters = new Dictionary<string, Secs4Net.Item>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["PPID"] = Secs4Net.Item.A(string.IsNullOrWhiteSpace(pj.RecipeId) ? "DEFAULT" : pj.RecipeId),
                        ["LOTID"] = Secs4Net.Item.A(request.LotId ?? string.Empty),
                        ["PORTID"] = Secs4Net.Item.A(request.PortId ?? string.Empty),
                        ["MODE"] = Secs4Net.Item.A(_device.Mode),
                        ["SLOTSLIST"] = Secs4Net.Item.A(string.Join(',', slots))
                    }
                };

                // 异步下发 PPSELECT，并使用任务缓存槽位（格式与 gRPC 一致："1,2,4,8"）。
                await _measurementDispatcher.DispatchProcessProgramSelectAsync(ppSelect, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("ProcessProgramSelect dispatched by CEID=1002. CJID={CJID}, PJID={PJID}, CarrierId={CarrierId}, PortId={PortId}, LotId={LotId}, SlotsList={SlotsList}", cj.CJID, pj.PJID, carrierId, request.PortId, request.LotId, string.Join(',', slots));
                return;
            }
        }
    }

    /// <summary>
    /// 校验结果事件中的 CarrierId 是否存在于 Host 下发的 S16F15/S14F9 作业计划。
    /// 作用：仅基于 CarrierId 做快速校验，不阻断 S6F11 上报链路。
    /// </summary>
    private bool TryValidateCarrierInPlannedJobsForResultEvent(IDictionary<string, string> parameters)
    {
        var carrierId = ReadParameter(parameters, "CARRIERID", "RFID")?.Trim();
        if (string.IsNullOrWhiteSpace(carrierId))
        {
            return false;
        }

        // 先匹配 S14F9 的 ControlJob CarrierInputSpec，命中即视为在计划内。
        var hitInControlJobs = _jobPlanStorage
            .GetAllControlJobs()
            .Any(cj => cj.CarrierInputSpec.Any(x => string.Equals(x, carrierId, StringComparison.OrdinalIgnoreCase)));
        if (hitInControlJobs)
        {
            return true;
        }

        // 兜底分支：再匹配 S16F15 的 ProcessJob Carriers，兼容仅下发 PJ 未下发 CJ 的现场时序。
        return _jobPlanStorage
            .GetAllProcessJobs()
            .Any(pj => pj.Carriers.Any(x => string.Equals(x.CarrierId, carrierId, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// 根据通用事件 CEID 执行 Port 上下文后置处理。
    /// 作用：在关键生命周期事件成功发送 S6F11 后，清理对应 Port 运行态，避免后续出现串批次。
    /// </summary>
    private void TryFinalizePortContextByEvent(uint ceid, IDictionary<string, string> parameters)
    {
        // if 关键分支：仅对 CarrierRemoved(1012) 与 LotCompleted(1005) 执行上下文清理。
        if (ceid is not 1012 and not 1005)
        {
            return;
        }

        var portId = ReadParameter(parameters, "PORTID", "PORT", "LOADPORT")?.Trim();
        if (string.IsNullOrWhiteSpace(portId))
        {
            // 兜底分支：未携带 PORTID 时尝试按 LOTID 反查 Port，上下文命中后再清理。
            var lotId = ReadParameter(parameters, "LOTID")?.Trim();
            if (!string.IsNullOrWhiteSpace(lotId) && _portContextStorage.TryGetByLotId(lotId, out var byLot))
            {
                portId = byLot.PortId;
            }
        }

        // if 关键分支：定位到 Port 后再删除上下文，避免误删。
        if (!string.IsNullOrWhiteSpace(portId))
        {
            _portContextStorage.RemoveByPortId(portId);
        }
    }

    /*
    /// <summary>
    /// 已停用：检测结果统一通过 ReportEvent（CEID=1001）上报。
    /// </summary>
    public Task<ReportReply> ResultReport(WaferMessage request, ServerCallContext context)
    {
        throw new NotSupportedException("ResultReport 已停用，请改用 ReportEvent(CEID=1001)。");
    }
    */

    public override async Task<AlarmReply> ReportAlarm(AlarmReportRequest request, ServerCallContext context)
    {
        // 打印入站 gRPC 报警参数，便于联调确认报警字段透传。
        _logger.LogInformation(
            "gRPC IN ReportAlarm. Peer={Peer}, Source={Source}, AlarmId={AlarmId}, AlarmCode={AlarmCode}, AlarmText={AlarmText}, Severity={Severity}",
            context.Peer,
            request.Source,
            request.AlarmId,
            request.AlarmCode,
            request.AlarmText,
            request.Severity);

        _alarmStore.Upsert(request);

        // 维护当前激活报警状态，供 S5F5/S5F6 查询链路使用。
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
        // 打印入站 gRPC 批次完成参数，便于联调确认结束态字段。
        _logger.LogInformation(
            "gRPC IN ReportLotCompleted. Peer={Peer}, PORTID={PortId}, LOTID={LotId}, STATUS={Status}",
            context.Peer,
            request.PortId,
            request.LotId,
            request.Status);

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
            Success = true,
            Code = 0,
            Message = string.Empty
        };
    }

    /// <summary>
    /// 请求切换远程在线状态。
    /// 规则：仅当当前为 OnLineLocal 时切换为 OnLineRemote 并返回成功。
    /// </summary>
    public override async Task<OnlineStatusReply> RequestOnlineStatus(OnlineStatusRequest request, ServerCallContext context)
    {
        // 打印入站 gRPC 在线切换请求，便于定位状态切换来源。
        _logger.LogInformation("gRPC IN RequestOnlineStatus. Peer={Peer}, Source={Source}, CurrentState={CurrentState}", context.Peer, request.Source, _device.IsOnline);

        // 1006 在线状态变化事件的触发来源固定为 Device，避免外部参数污染。
        const string stateChangeTrigger = "Device";

        // if 关键分支：当前处于 OnLineLocal 时允许切换为 OnLineRemote。本地转远程
        if (_device.IsOnline == DeviceOnlineState.OnLineLocal && request.Source == "remote")
        {
            var fromState = _device.IsOnline.ToString();
            _device.IsOnline = DeviceOnlineState.OnLineRemote;
            var toState = _device.IsOnline.ToString();

            // 状态切换成功后，按预设模板发送 OnlineStateChanged 的 S6F11（CEID=1021）。
            var stateChangedData = ActiveReportSxFyFunctions.BuildOnlineStateChangedReport(
                _secsGemContext.GetNextDataId(),
                fromState,
                toState,
                stateChangeTrigger,
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

            // 状态切换成功后，按预设模板发送 OnlineStateChanged 的 S6F11（CEID=1021）。
            var stateChangedData = ActiveReportSxFyFunctions.BuildOnlineStateChangedReport(
                _secsGemContext.GetNextDataId(),
                fromState,
                toState,
                stateChangeTrigger,
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

                // 异步下发 PPSELECT，先把配方/批次/端口/槽位上下文写入设备侧，作为 START 前置条件。
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

                // 在 PPSELECT 成功后异步下发 START，驱动设备执行对应 ProcessJob。
                await _measurementDispatcher.DispatchStartMeasurementAsync(start, cancellationToken).ConfigureAwait(false);

                _jobPlanStorage.MarkProcessJobAutoStarted(pj.PJID, request.PortId ?? string.Empty, request.LotId ?? string.Empty);
                _logger.LogInformation("Auto dispatched planned job on carrier arrived. CJID={CJID}, PJID={PJID}, CarrierId={CarrierId}, PortId={PortId}, LotId={LotId}", cj.CJID, pj.PJID, carrierId, request.PortId, request.LotId);
                return;
            }
        }
    }

    /// <summary>
    /// 从通用事件参数中增量刷新 Port 上下文。
    /// 作用：不依赖 CEID/VID 绑定关系，仅按参数名识别 PORTID/LOTID/RFID/SLOTSLIST，避免运行态上下文滞后。
    /// </summary>
    private void TryUpdatePortContextFromEventParameters(IDictionary<string, string> parameters)
    {
        // if 关键分支：参数为空时不更新上下文。
        if (parameters is null || parameters.Count == 0)
        {
            return;
        }

        // 按业务参数名读取上下文字段，不依赖 VID 或 RPTID 映射。
        var portId = ReadParameter(parameters, "PORTID", "PORT", "LOADPORT")?.Trim();
        var lotId = ReadParameter(parameters, "LOTID")?.Trim();
        var carrierId = ReadParameter(parameters, "CARRIERID", "RFID")?.Trim();
        var slotsList = ReadParameter(parameters, "SLOTSLIST", "SLOTS")?.Trim();

        // if 关键分支：未提供 PORTID 时，尝试按 LOTID 回查已存在上下文的 PORTID。
        if (string.IsNullOrWhiteSpace(portId)
            && !string.IsNullOrWhiteSpace(lotId)
            && _portContextStorage.TryGetByLotId(lotId, out var byLot))
        {
            portId = byLot.PortId;
        }

        // if 关键分支：仍无法定位 PORTID 时不更新，避免误写未知端口。
        if (string.IsNullOrWhiteSpace(portId))
        {
            return;
        }

        // 增量合并上下文；新值为空时保留旧值，确保不同事件可分步补齐上下文字段。
        if (_portContextStorage.TryGetByPortId(portId, out var existing))
        {
            _portContextStorage.Upsert(new PortRuntimeContext
            {
                PortId = portId,
                LotId = string.IsNullOrWhiteSpace(lotId) ? existing.LotId : lotId,
                CarrierId = string.IsNullOrWhiteSpace(carrierId) ? existing.CarrierId : carrierId,
                SlotsList = string.IsNullOrWhiteSpace(slotsList) ? existing.SlotsList : slotsList,
                UpdatedAtUtc = DateTime.UtcNow
            });
            return;
        }

        _portContextStorage.Upsert(new PortRuntimeContext
        {
            PortId = portId,
            LotId = lotId ?? string.Empty,
            CarrierId = carrierId ?? string.Empty,
            SlotsList = slotsList ?? string.Empty,
            UpdatedAtUtc = DateTime.UtcNow
        });
    }

    /// <summary>
    /// 按候选键名读取事件参数值（忽略大小写）。
    /// 作用：兼容不同设备对同一字段的命名差异（如 PORTID/PORT/LOADPORT）。
    /// </summary>
    private static string? ReadParameter(IDictionary<string, string> parameters, params string[] keys)
    {
        // if 关键分支：参数或候选键为空时直接返回空。
        if (parameters is null || keys is null || keys.Length == 0)
        {
            return null;
        }

        foreach (var key in keys)
        {
            // if 关键分支：命中参数键时返回对应值。
            if (parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            // 兜底分支：参数字典键可能大小写不一致，逐项比较以提升兼容性。
            var matched = parameters.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(matched.Value))
            {
                return matched.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// 将入站 gRPC 参数字典格式化为简洁日志文本。
    /// 作用：统一打印 key=value，便于快速排查参数缺失与命名差异问题。
    /// </summary>
    private static string ToParameterLogText(IDictionary<string, string> parameters)
    {
        // if 关键分支：空参数时直接输出占位文本，避免日志为空难以识别。
        if (parameters is null || parameters.Count == 0)
        {
            return "<empty>";
        }

        // 按键名排序后输出，确保同一请求的日志顺序稳定。
        return string.Join(", ", parameters
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => $"{kv.Key}={kv.Value}"));
    }
}
