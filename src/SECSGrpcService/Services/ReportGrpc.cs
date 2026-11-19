using Google.Protobuf;
using Google.Protobuf.Collections;
using Grpc.Core;
using Grpc.Net.Client;
using GY.EFEM;
using GY.SECSserver;
using Nacos.V2;
using Secs4Net;
using SECSdata;
using SECShandler.Functions;
using SECShandler.Interfaces;

namespace SECSGrpcService.Services;

/// <summary>
/// 上报相关 gRPC 服务实现，对应 <c>efem.proto</c>。
/// </summary>
public sealed class ReportGrpc : ReportGrpcService.ReportGrpcServiceBase
{
    private readonly ILogger<ReportGrpc> _logger;
    private readonly SecsGemContext _secsGemContext;
    private readonly AlarmStore _alarmStore;
    private readonly AlarmCodeMap _alarmCodeMap;
    private readonly VidMap _vidMap;
    private readonly CeidMap _ceidMap;
    private readonly IActiveSxFyDispatcher _activeSxFyDispatcher;
    private readonly IDevice _device;
    private readonly IEventLinkStorage _eventLinkStorage;
    private readonly IAlarmEnableStorage _alarmEnableStorage;
    private readonly IAlarmStateStorage _alarmStateStorage;
    private readonly IPortContextStorage _portContextStorage;
    private readonly IJobPlanStorage _jobPlanStorage;
    private readonly ICommandDispatcher _measurementDispatcher;
    private readonly IReportStorage _reportStorage;
    private readonly IVidValueStorage _vidValueStorage;
    private readonly ITypedVidValueStorage _typedVidValueStorage;
    private readonly Nacos.V2.INacosNamingService _nacos;

    /// <summary>
    /// 初始化上报服务及独立的类型化 VID 缓存，保留原字符串 VID 存储依赖。
    /// </summary>
    public ReportGrpc(
        ILogger<ReportGrpc> logger,
        SecsGemContext secsGemContext,
        AlarmStore alarmStore,
        AlarmCodeMap alarmCodeMap,
        VidMap vidMap,
        CeidMap ceidMap,
        IActiveSxFyDispatcher activeSxFyDispatcher,
        IDevice device,
        IEventLinkStorage eventLinkStorage,
        IAlarmEnableStorage alarmEnableStorage,
        IAlarmStateStorage alarmStateStorage,
        IPortContextStorage portContextStorage,
        IJobPlanStorage jobPlanStorage,
        ICommandDispatcher measurementDispatcher,
        IReportStorage reportStorage,
        IVidValueStorage vidValueStorage,
        INacosNamingService nacos,
        ITypedVidValueStorage typedVidValueStorage)
    {
        _logger = logger;
        _secsGemContext = secsGemContext;
        _alarmStore = alarmStore;
        _alarmCodeMap = alarmCodeMap;
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
        _reportStorage = reportStorage;
        _vidValueStorage = vidValueStorage;
        _nacos = nacos;
        _typedVidValueStorage = typedVidValueStorage;
    }

    /// <summary>
    /// 通用事件上报：EFEM 传 EventName + 参数，本项目通过 CEID.csv 映射到 CEID 并发送 S6F11。
    /// 类型化参数转换成功后旁路缓存，不改变原字符串 VID 存储及本次上报的取值方式。
    /// </summary>
    public override async Task<ReportReply> ReportEvent(GenericEventReportRequest request, ServerCallContext context)
    {
        var eventName = request.EventName;
        var ceid = _ceidMap.TryGetCeid(eventName, out var mappedCeid) ? mappedCeid : 0;
        // 打印入站 gRPC 通用事件参数，便于排查 CEID 对应值是否正确传入。
        _logger.LogInformation(
            "gRPC IN ReportEvent. Peer={Peer}, CEID={CEID}, Params={Params}, TypeParams={TypeParams}",
            context.Peer,
            ceid,
            ToParameterLogText(request.Parameters),
            ToParameterLogText(request.TypedParameters));

        // 通用事件上报前先刷新 VID 动态值表，为S1F11和S6F11提供数据。
        UpdateVidRuntimeValues(request.Parameters);

        // 请求 CEID 未在 CEID.csv 中定义时直接拒绝，避免上报未注册事件。
        if (!_ceidMap.TryGetEventName(ceid, out eventName))
        {
            return new ReportReply
            {
                Success = false,
                Code = 1,
                Message = $"未知事件名：{ceid}"
            };
        }

        // 按请求 CEID 查询 Host 运行态绑定的 RPTID，保持 S2F35 动态绑定语义。
        var linkedRptIds = _eventLinkStorage.GetRptIdsForCeid(ceid);
        if (linkedRptIds is null || linkedRptIds.Count == 0)
        {
            return new ReportReply
            {
                Success = false,
                Code = 2,
                Message = $"CEID {ceid} 未绑定 RPTID。"
            };
        }

        // 新版自变量字典，可存储多种数据类型
        var typedValues = new Dictionary<ushort, Item>();
        // for each 关键分支：仅转换能在 VID 映射中找到的类型化参数，保留 SECS-II 原始类型。
        foreach (var kv in request.TypedParameters)
        {
            if (!_vidMap.TryGetVid(kv.Key, out var vid))
            {
                _logger.LogWarning("Typed event parameter {Name} is not mapped to a VID.", kv.Key);
                continue;
            }

            try
            {
                typedValues[vid] = ConvertTypedEventValue(kv.Value);
            }
            catch (InvalidOperationException ex)
            {
                return new ReportReply { Success = false, Code = 4, Message = $"参数 {kv.Key} 类型无效: {ex.Message}" };
            }
        }

        // 全部参数转换成功后再更新独立缓存，不将类型化值写入原字符串存储。
        foreach (var (vid, value) in typedValues)
        {
            _typedVidValueStorage.UpsertByVid(vid, value);
        }

        // 最小实现取首个绑定 RPTID 作为通用事件上报 RPTID。后续改为遍历RPTID列表，按 Host 配置的上报项组合发送多条 S6F11。
        foreach (var rptId in linkedRptIds)
        {
            // 旧版自变量字典，只可存储字符串数据类型<name,value>
            var mergedParameters = GetVidMapByRptId(rptId, request.Parameters, typedValues.Keys.ToHashSet());
            var data = ActiveReportSxFyFunctions.BuildGenericEventReport(
                _secsGemContext.GetNextDataId(),
                ceid,
                rptId,
                mergedParameters,
                TryGetVid,
                vid => typedValues.TryGetValue(vid, out var value) ? value : null);
            // 异步发送通用事件 S6F11，上报是否成功仍受 S2F33/S2F35/S2F37 门禁控制。
            var sent = await _activeSxFyDispatcher.SendS6F11Async(data, context.CancellationToken).ConfigureAwait(false);
            if (!sent)
            {
                return new ReportReply
                {
                    Success = false,
                    Code = 3,
                    Message = $"事件 {eventName}({ceid}) 上报失败或被门禁拦截。"
                };
            }
        }
        return new ReportReply { Success = true, Code = 0, Message = "OK" };
    }

    public override async Task<AlarmReply> ReportAlarm(AlarmReportRequest request, ServerCallContext context)
    {
        // 请求文本优先；请求未提供时按 AlarmId 查询 CSV，仍未命中则使用默认提示。
        if (string.IsNullOrWhiteSpace(request.AlarmText))
        {
            request.AlarmText = _alarmCodeMap.TryGetAlarmText(request.AlarmId, out string csvAlarmText)
                && !string.IsNullOrWhiteSpace(csvAlarmText)
                    ? csvAlarmText
                    : "unknow alarm";
        }

        // 打印入站 gRPC 报警参数，便于联调确认报警字段透传。
        _logger.LogInformation(
            "gRPC IN ReportAlarm. Peer={Peer}, Source={Source}, AlarmId={AlarmId}, AlarmCode={AlarmCode}, AlarmText={AlarmText}, Severity={Severity}",
            context.Peer,
            request.Source,
            request.AlarmId,
            request.AlarmCode,
            request.AlarmText,
            request.Severity);

        // 将 gRPC int32 报警字段转换为内部及 SECS 消息要求的无符号类型。
        uint alarmId = unchecked((uint)request.AlarmId);
        byte alarmCode = unchecked((byte)request.AlarmCode);

        // 存入并更新内存中的Alarm表，以及添加数据库
        _alarmStore.Upsert(request);

        // 维护当前激活报警状态，供 S5F5/S5F6 查询链路使用。
        _alarmStateStorage.UpsertAlarm(
            alarmId,
            alarmCode,
            request.AlarmText ?? string.Empty);

        _logger.LogInformation(
            "ReportAlarm received. Source={Source}, AlarmId={AlarmId}, AlarmCode={AlarmCode}, Severity={Severity}, Peer={Peer}",
            request.Source,
            request.AlarmId,
            request.AlarmCode,
            request.Severity,
            context.Peer);

        //Console.WriteLine($"Time：{request.OccurredAtUnixMs}进入了报警上报 ReportAlarm，source:{request.Source}, alarmId:{request.AlarmId}, alarmCode:{request.AlarmCode}");

        // Host 配置禁用该报警 ID 时不发送 S5F1，仅保留本地追溯。
        if (!_alarmEnableStorage.IsAlarmEnabled(alarmId))
        {
            _logger.LogInformation("Alarm send disabled by host config. ALID={ALID}", alarmId);
            return new AlarmReply
            {
                MessageCode = 0,
                Message = "OK",
                RequestId = Guid.NewGuid().ToString("N")
            };
        }

        // 存在活动 SECS 会话时发送 S5F1，否则仅记录警告并返回。
        if (_secsGemContext.TryGet(out var secsGem) && secsGem is not null)
        {
            string alarmCodeText = request.AlarmCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var s5f1Data = new S5F1_data
            {
                ALCD = alarmCode,
                ALID = alarmId,
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
    /// 请求切换远程在线状态。
    /// 规则：仅当当前为 OnLineLocal 时切换为 OnLineRemote 并返回成功。
    /// </summary>
    public override async Task<OnlineStatusReply> RequestOnlineStatus(OnlineStatusRequest request, ServerCallContext context)
    {
        // 打印入站 gRPC 在线切换请求，便于定位状态切换来源。
        _logger.LogInformation("gRPC IN RequestOnlineStatus. Peer={Peer}, Source={Source}, CurrentState={CurrentState}", context.Peer, request.Source, _device.IsOnline);

        // 1029 在线状态变化事件的触发来源固定为 Device
        const string stateChangeTrigger = "Device";

        // 当前处于 OnLineLocal 时允许切换为 OnLineRemote。本地转远程
        if (_device.IsOnline == DeviceOnlineState.OnLineLocal && request.Source == "remote")
        {
            _device.IsOnline = DeviceOnlineState.OnLineRemote;
            _vidValueStorage.UpsertByVid(1002, "Remote");
            // 状态切换成功后，按 Host 运行态绑定动态构建并发送 OnlineStateChanged 的 S6F11。
            // 异步调用说明：发送状态切换事件到 Host，是否成功受 S2F33/S2F35/S2F37 门禁控制。
            var sent = await TrySendOnlineStateChangedByRuntimeBindingAsync(stateChangeTrigger, context.CancellationToken).ConfigureAwait(false);
            if (sent)
            {
                _logger.LogInformation("Online state changed reported. CEID={CEID}, ChangeTrigger={ChangeTrigger}, Online={Online}, Remote={Remote}", 1029, stateChangeTrigger, _device.IsOnline, _device.IsOnline == DeviceOnlineState.OnLineRemote);
            }
            else
            {
                _logger.LogWarning("Online state changed report skipped/failed. CEID={CEID}, ChangeTrigger={ChangeTrigger}, Online={Online}, Remote={Remote}", 1029, stateChangeTrigger, _device.IsOnline, _device.IsOnline == DeviceOnlineState.OnLineRemote);
            }

            return new OnlineStatusReply
            {
                MessageCode = 0,
                Message = "切换成功，已进入远程在线状态。",
                Online = true
            };
            // 远程转本地
        }
        else if (_device.IsOnline == DeviceOnlineState.OnLineRemote && request.Source == "local")
        {
            _device.IsOnline = DeviceOnlineState.OnLineLocal;
            _vidValueStorage.UpsertByVid(1002, "Local");
            // 状态切换成功后，按 Host 运行态绑定动态构建并发送 OnlineStateChanged 的 S6F11。
            // 异步调用说明：发送状态切换事件到 Host，是否成功受 S2F33/S2F35/S2F37 门禁控制。
            var sent = await TrySendOnlineStateChangedByRuntimeBindingAsync(stateChangeTrigger, context.CancellationToken).ConfigureAwait(false);
            if (sent)
            {
                _logger.LogInformation("Online state changed reported. CEID={CEID}, ChangeTrigger={ChangeTrigger}, Online={Online}, Remote={Remote}", 1006, stateChangeTrigger, _device.IsOnline, _device.IsOnline == DeviceOnlineState.OnLineRemote);
            }
            else
            {
                _logger.LogWarning("Online state changed report skipped/failed. CEID={CEID}, ChangeTrigger={ChangeTrigger}, Online={Online}, Remote={Remote}", 1006, stateChangeTrigger, _device.IsOnline, _device.IsOnline == DeviceOnlineState.OnLineRemote);
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
            // 当前为 OffLine 时拒绝切换并提示。
            return new OnlineStatusReply
            {
                MessageCode = 1,
                Message = "目前为本地在线状态，无需切换。",
                Online = true
            };
        }
        else if (_device.IsOnline == DeviceOnlineState.OnLineRemote && request.Source == "remote")
        {
            // 当前为 OffLine 时允许切换为 OnLineLocal。
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
    /// 按 Host 运行态绑定发送在线状态变更事件。
    /// 作用：移除固定模板，统一走 CEID->RPTID->VID 动态上报链路。
    /// </summary>
    private async Task<bool> TrySendOnlineStateChangedByRuntimeBindingAsync(string trigger, CancellationToken cancellationToken)
    {
        const uint onlineStateChangedCeid = 1029;

        var linkedRptIds = _eventLinkStorage.GetRptIdsForCeid(onlineStateChangedCeid);
        if (linkedRptIds is null || linkedRptIds.Count == 0)
        {
            return false;
        }

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ChangeTrigger"] = trigger
        };

        UpdateVidRuntimeValues(parameters);

        var rptId = linkedRptIds[0];
        var mergedParameters = GetVidMapByRptId(rptId, parameters);
        var data = ActiveReportSxFyFunctions.BuildGenericEventReport(
            _secsGemContext.GetNextDataId(),
            onlineStateChangedCeid,
            rptId,
            mergedParameters,
            TryGetVid);

        // 异步调用说明：发送状态切换类 S6F11，交由主动发送器处理会话与门禁策略。
        return await _activeSxFyDispatcher.SendS6F11Async(data, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 更新 VID 动态值表。
    /// 作用：把入站参数写入运行时缓存，供后续不同 RPTID/VID 组合复用最新值。
    /// </summary>
    private void UpdateVidRuntimeValues(IEnumerable<KeyValuePair<string, string>> parameters)
    {
        foreach (var kv in parameters)
        {
            if (string.IsNullOrWhiteSpace(kv.Key))
            {
                continue;
            }

            var value = kv.Value ?? string.Empty;
            _vidValueStorage.UpsertByName(kv.Key, value);

            if (_vidMap.TryGetVid(kv.Key, out var vid))
            {
                _vidValueStorage.UpsertByVid(vid, value);
            }
        }
    }

    /// <summary>
    /// 按 RPTID 的 VID 定义合并参数。
    /// 作用：当本次请求缺少某些 VID 对应值时，从动态值表回填，适配 Host 侧动态上报组合。
    /// parameters : 设备上传的VID，NAME：VALUE。
    /// report: 依据rptid返回的VID列表，VID：VALUE。
    /// _vidMap： VID映射表，name：vid。
    /// _vidValueStorage： VID动态值表，VID、NAME：VALUE。
    /// </summary>
    private IReadOnlyDictionary<string, string> GetVidMapByRptId(uint rptId, IEnumerable<KeyValuePair<string, string>> parameters, HashSet<ushort>? typedVids = null)
    {
        var report = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 遍历参数，将内存中的vidMap进行刷新
        foreach (var kv in parameters)
        {
            if (string.IsNullOrWhiteSpace(kv.Key))
            {
                continue;
            }
            // 刷新值
            _vidValueStorage.UpsertByName(kv.Key, kv.Value ?? string.Empty);
        }

        // 按当前 RPTID 的 VID 清单补齐缺失项，优先使用按 VID 缓存的值。
        foreach (var vidRaw in _reportStorage.GetVidsForReport(rptId))
        {
            // RPT对应的VID
            var vid = (ushort)vidRaw;
            if (!_vidMap.TryGetName(vid, out var cpName) || string.IsNullOrWhiteSpace(cpName))
            {
                _logger.LogWarning("VID {VID} 未在 VID.csv 中定义对应参数名，忽略补齐。", vid);
                continue;
            }
            // if 关键分支：类型化值不转为字符串；占位键仅用于维持 RPTID 定义的 VID 顺序。
            if (typedVids?.Contains(vid) == true)
            {
                report[cpName] = string.Empty;
                continue;
            }

            // 获取值
            if (_vidValueStorage.TryGetByVid(vid, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                report[cpName] = value;
                continue;
            }
        }

        return report;
    }

    /// <summary>将 gRPC 类型化参数递归转换为 SECS-II Item，不根据参数名猜测数据类型。</summary>
    private static Item ConvertTypedEventValue(EventParameterValue value)
    {
        // switch 关键分支：oneof 明确提供类型；未设置的分支不能默认为 ASCII。
        return value.ValueCase switch
        {
            EventParameterValue.ValueOneofCase.Text => Item.A(value.Text),
            EventParameterValue.ValueOneofCase.SignedInteger => BuildSignedEventValue(value.SignedInteger, value.SecsFormat),
            EventParameterValue.ValueOneofCase.UnsignedInteger => BuildUnsignedEventValue(value.UnsignedInteger, value.SecsFormat),
            EventParameterValue.ValueOneofCase.FloatingPoint => BuildFloatingEventValue(value.FloatingPoint, value.SecsFormat),
            EventParameterValue.ValueOneofCase.BooleanValue => Item.Boolean(value.BooleanValue),
            EventParameterValue.ValueOneofCase.Binary => Item.B(value.Binary.ToByteArray()),
            EventParameterValue.ValueOneofCase.List => Item.L(value.List.Items.Select(ConvertTypedEventValue).ToArray()),
            _ => throw new InvalidOperationException("oneof value must be set")
        };
    }

    /// <summary>按显式 SECS-II 格式编码有符号整数，校验目标宽度范围。</summary>
    private static Item BuildSignedEventValue(long value, string format)
    {
        // switch 关键分支：空格式沿用 I8；溢出或不匹配时拒绝而不是截断数值。
        return format.Trim().ToUpperInvariant() switch
        {
            "" => Item.I4((int)value),
            "I1" when value is >= sbyte.MinValue and <= sbyte.MaxValue => Item.I1((sbyte)value),
            "I2" when value is >= short.MinValue and <= short.MaxValue => Item.I2((short)value),
            "I4" when value is >= int.MinValue and <= int.MaxValue => Item.I4((int)value),
            //"I8" when value is >= long.MinValue and <= long.MaxValue => Item.I8((long)value),//暂不使用
            _ => throw new InvalidOperationException($"Invalid signed integer format or value: {format}")
        };
    }

    /// <summary>按显式 SECS-II 格式编码无符号整数，校验目标宽度范围。</summary>
    private static Item BuildUnsignedEventValue(ulong value, string format)
    {
        // switch 关键分支：空格式沿用 U8；溢出或不匹配时拒绝而不是截断数值。
        return format.Trim().ToUpperInvariant() switch
        {
            "" => Item.U4((uint)value),
            "U1" when value <= byte.MaxValue => Item.U1((byte)value),
            "U2" when value <= ushort.MaxValue => Item.U2((ushort)value),
            "U4" when value <= uint.MaxValue => Item.U4((uint)value),
            //"U8" when value <= ulong.MaxValue => Item.U8((ulong)value),//暂不使用
            _ => throw new InvalidOperationException($"Invalid unsigned integer format or value: {format}")
        };
    }

    /// <summary>按显式 SECS-II 格式编码浮点数，避免 F4 范围溢出。</summary>
    private static Item BuildFloatingEventValue(double value, string format)
    {
        // switch 关键分支：空格式沿用 F8；F4 不接受会溢出的有限值。
        return format.Trim().ToUpperInvariant() switch
        {
            "" or "F8" => Item.F8(value),
            "F4" when !double.IsFinite(value) || Math.Abs(value) <= float.MaxValue => Item.F4((float)value),
            _ => throw new InvalidOperationException($"Invalid floating-point format or value: {format}")
        };
    }

    /// <summary>
    /// VID 映射委托，供主动外发打包函数注入。
    /// </summary>
    private (bool Found, ushort Vid) TryGetVid(string cpName)
    {
        // 映射命中则返回对应 VID。
        if (_vidMap.TryGetVid(cpName, out var vid))
        {
            return (true, vid);
        }

        // 兜底分支：未命中映射时返回 false 与 0。
        return (false, 0);
    }

    /// <summary>
    /// 将报警代码字节转换为可读字符串。
    /// 作用：优先按 UTF8 解码，失败时回退十六进制，便于查询结果统一展示。
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
    /// 从通用事件参数中增量刷新 Port 上下文。
    /// 作用：不依赖 CEID/VID 绑定关系，仅按参数名识别 PORTID/LOTID/RFID/SLOTSLIST，避免运行态上下文滞后。
    /// </summary>
    private void TryUpdatePortContextFromEventParameters(IDictionary<string, string> parameters)
    {
        // 参数为空时不更新上下文。
        if (parameters is null || parameters.Count == 0)
        {
            return;
        }

        // 按业务参数名读取上下文字段，不依赖 VID 或 RPTID 映射。
        var portId = ReadParameter(parameters, "PORTID", "PORT", "LOADPORT")?.Trim();
        var lotId = ReadParameter(parameters, "LOTID")?.Trim();
        var carrierId = ReadParameter(parameters, "CARRIERID", "RFID")?.Trim();
        var slotsList = ReadParameter(parameters, "SLOTSLIST", "SLOTS")?.Trim();

        // 未提供 PORTID 时，尝试按 LOTID 回查已存在上下文的 PORTID。
        if (string.IsNullOrWhiteSpace(portId)
            && !string.IsNullOrWhiteSpace(lotId)
            && _portContextStorage.TryGetByLotId(lotId, out var byLot))
        {
            portId = byLot.PortId;
        }

        // 仍无法定位 PORTID 时不更新，避免误写未知端口。
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
        // 参数或候选键为空时直接返回空。
        if (parameters is null || keys is null || keys.Length == 0)
        {
            return null;
        }

        foreach (var key in keys)
        {
            // 命中参数键时返回对应值。
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
        // 空参数时直接输出占位文本，避免日志为空难以识别。
        if (parameters is null || parameters.Count == 0)
        {
            return "<empty>";
        }

        // 按键名排序后输出，确保同一请求的日志顺序稳定。
        return string.Join(", ", parameters
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => $"{kv.Key}={kv.Value}"));
    }

    /// <summary>
    /// 将类型化参数按名称排序并输出 Protobuf JSON，保留 oneof 值、SECS 格式及嵌套列表用于诊断。
    /// </summary>
    private static string ToParameterLogText(MapField<string, EventParameterValue>? typedParameters)
    {
        // 空参数沿用字符串参数的占位格式，避免日志格式化中断事件上报。
        if (typedParameters is null || typedParameters.Count == 0)
        {
            return "<empty>";
        }

        // 使用 Protobuf 序列化规则处理长整数、布尔、Base64 二进制及递归列表，不进行业务类型转换。
        return string.Join(", ", typedParameters
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => $"{kv.Key}={(kv.Value is null ? "<null>" : JsonFormatter.Default.Format(kv.Value))}"));
    }
}
