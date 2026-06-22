using Secs4Net;
using SECSdata;
using SECShandler.Interfaces;

namespace SECSGrpcService.Services;

public sealed class GrpcMeasurementDispatcher : IMeasurementDispatcher
{
    private readonly IConfiguration _configuration;
    private readonly SecsEfemGrpc _secsEfemGrpc;
    private readonly IDevice _device;
    private readonly ILogger<GrpcMeasurementDispatcher> _logger;
    private readonly IPortContextStorage _portContextStorage;

    public GrpcMeasurementDispatcher(
        IConfiguration configuration,
        SecsEfemGrpc secsEfemGrpc,
        IDevice device,
        ILogger<GrpcMeasurementDispatcher> logger,
        IPortContextStorage portContextStorage)
    {
        _configuration = configuration;
        _secsEfemGrpc = secsEfemGrpc;
        _device = device;
        _logger = logger;
        _portContextStorage = portContextStorage;
    }

    public async Task DispatchStartMeasurementAsync(S2F41_data data, CancellationToken cancellationToken)
    {
        var (targetServiceName, targetGroup, targetClusters, targetUseHttps, fallbackAddresses) = GetTargetOptions();
        var wafer = ToWaferMessage(data);

        // 方法关键节点：START 前拉取一次状态，避免设备状态不满足时误启动。
        var status = await _secsEfemGrpc.GetStatusFromServiceAsync(
            targetServiceName,
            targetGroup,
            targetClusters,
            targetUseHttps,
            fallbackAddresses,
            cancellationToken);

        // if 关键分支：状态拉取失败时拒绝启动，避免盲发 START。
        if (status is null)
        {
            throw new InvalidOperationException("START rejected: EFEM StatusReport failed before start.");
        }

        var runStatus = (status.RunStatus ?? string.Empty).Trim().ToLowerInvariant();
        if (runStatus is "running" or "jam")
        {
            throw new InvalidOperationException($"START rejected: EFEM RunStatus={status.RunStatus}.");
        }

        _logger.LogInformation("S2F41 START -> StartMeasurement trigger. LotId={LotId}, Mode={Mode}", wafer.LotId, _device.Mode);

        await _secsEfemGrpc.SendStartMeasurementToServiceAsync(
            targetServiceName,
            targetGroup,
            targetClusters,
            targetUseHttps,
            fallbackAddresses,
            cancellationToken);
    }

    public async Task DispatchStopMeasurementAsync(S2F41_data data, CancellationToken cancellationToken)
    {
        var (targetServiceName, targetGroup, targetClusters, targetUseHttps, fallbackAddresses) = GetTargetOptions();
        var wafer = ToWaferMessage(data);

        _logger.LogInformation("S2F41 STOP -> StopMeasurement payload. LotId={LotId}, SlotId={SlotId}", wafer.LotId, wafer.SlotId);

        await _secsEfemGrpc.SendStopMeasurementToServiceAsync(targetServiceName, targetGroup, targetClusters, targetUseHttps, wafer, fallbackAddresses, cancellationToken);
    }

    public async Task DispatchPauseMeasurementAsync(S2F41_data data, CancellationToken cancellationToken)
    {
        var (targetServiceName, targetGroup, targetClusters, targetUseHttps, fallbackAddresses) = GetTargetOptions();
        var wafer = ToWaferMessage(data);

        _logger.LogInformation("S2F41 PAUSE -> PauseMeasurement payload. WaferId={WaferId}, SlotId={SlotId}, LotId={LotId}", wafer.WaferId, wafer.SlotId, wafer.LotId);

        await _secsEfemGrpc.SendPauseMeasurementToServiceAsync(targetServiceName, targetGroup, targetClusters, targetUseHttps, wafer, fallbackAddresses, cancellationToken);
    }

    public async Task DispatchResumeMeasurementAsync(S2F41_data data, CancellationToken cancellationToken)
    {
        var (targetServiceName, targetGroup, targetClusters, targetUseHttps, fallbackAddresses) = GetTargetOptions();
        var wafer = ToWaferMessage(data);

        _logger.LogInformation("S2F41 RESUME -> ResumeMeasurement payload. WaferId={WaferId}, SlotId={SlotId}, LotId={LotId}", wafer.WaferId, wafer.SlotId, wafer.LotId);

        await _secsEfemGrpc.SendResumeMeasurementToServiceAsync(targetServiceName, targetGroup, targetClusters, targetUseHttps, wafer, fallbackAddresses, cancellationToken);
    }

    public async Task DispatchProcessProgramSelectAsync(S2F41_data data, CancellationToken cancellationToken)
    {
        var (targetServiceName, targetGroup, targetClusters, targetUseHttps, fallbackAddresses) = GetTargetOptions();
        var wafer = ToWaferMessage(data);

        // 方法关键节点：PPSELECT 必须携带 PORTID，作为多 LoadPort 场景的唯一上下文锚点。
        var portIdFromCommand = ReadStringParameter(data.Parameters, "PORTID", "PORT", "LOADPORT")?.Trim();
        if (string.IsNullOrWhiteSpace(portIdFromCommand))
        {
            throw new InvalidOperationException("PPSELECT rejected: missing required parameter PORTID.");
        }

        // 方法关键节点：PPSELECT 的 LOTID 优先取命令参数；缺失时允许从 ReportRFID 已写入的 Port 上下文兜底。
        var lotIdFromCommand = (wafer.LotId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(lotIdFromCommand)
            && _portContextStorage.TryGetByPortId(portIdFromCommand, out var byPortContext)
            && !string.IsNullOrWhiteSpace(byPortContext.LotId))
        {
            lotIdFromCommand = byPortContext.LotId.Trim();
        }

        // 方法关键节点：优先读取 S2F41 中的 MODE 参数（VID=2001 对应语义），
        // 在 PPSELECT 阶段提前下发到设备端作为运行前准备。
        var modeFromCommand = ReadStringParameter(data.Parameters, "MODE");
        if (!string.IsNullOrWhiteSpace(modeFromCommand))
        {
            _device.Mode = modeFromCommand.Trim();
        }

        var slots = ReadSlotsParameter(data.Parameters).ToList();
        _device.SlotsList = slots;

        // 方法关键节点：PPSELECT 接收后同步刷新 Port 上下文中的 LOTID，供后续 Carrier 事件与调度链路复用。
        if (_portContextStorage.TryGetByPortId(portIdFromCommand, out var existingContext))
        {
            _portContextStorage.Upsert(new PortRuntimeContext
            {
                PortId = portIdFromCommand,
                LotId = lotIdFromCommand,
                CarrierId = existingContext.CarrierId,
                SlotsList = slots.Count > 0 ? string.Join(',', slots) : existingContext.SlotsList
            });
        }
        else
        {
            _portContextStorage.Upsert(new PortRuntimeContext
            {
                PortId = portIdFromCommand,
                LotId = lotIdFromCommand,
                CarrierId = string.Empty,
                SlotsList = slots.Count > 0 ? string.Join(',', slots) : string.Empty
            });
        }

        var recipe = new RecipeMessage
        {
            PPID = wafer.PPID,
            Mode = _device.Mode,
            LotId = lotIdFromCommand,
            PortId = portIdFromCommand
        };
        recipe.SlotsList.Add(_device.SlotsList);

        var slotsText = recipe.SlotsList.Count > 0 ? string.Join(',', recipe.SlotsList) : "<empty>";
        _logger.LogInformation("S2F41 PPSELECT -> ProcessProgramSelect payload. PPID={PPID}, Mode={Mode}, PortId={PortId}, LotId={LotId}, SlotsList={Slots}", recipe.PPID, recipe.Mode, recipe.PortId, recipe.LotId, slotsText);

        await _secsEfemGrpc.SendProcessProgramSelectToServiceAsync(targetServiceName, targetGroup, targetClusters, targetUseHttps, recipe, fallbackAddresses, cancellationToken);
    }

    public async Task DispatchSlotMapSelectAsync(S3F17_data data, CancellationToken cancellationToken)
    {
        var (targetServiceName, targetGroup, targetClusters, targetUseHttps, fallbackAddresses) = GetTargetOptions();

        // 方法关键节点：S3F17 对应独立槽位选择链路，和 PPSELECT 分离。
        var message = new SlotMapSelectMessage
        {
            PortId = string.Empty,
            LotId = data.LOTID ?? string.Empty
        };
        message.SlotsList.Add(data.SlotMap);

        var slotsText = message.SlotsList.Count > 0 ? string.Join(',', message.SlotsList) : "<empty>";
        _logger.LogInformation("S3F17 -> SlotMapSelect payload. LotId={LotId}, SlotsList={Slots}", message.LotId, slotsText);

        await _secsEfemGrpc.SendSlotMapSelectToServiceAsync(
            targetServiceName,
            targetGroup,
            targetClusters,
            targetUseHttps,
            message,
            fallbackAddresses,
            cancellationToken);
    }

    private (string ServiceName, string GroupName, string[] Clusters, bool UseHttps, string[] FallbackAddresses) GetTargetOptions()
    {
        var targetServiceName = _configuration.GetValue<string>("SecsListener:GrpcTargetServiceName");
        if (string.IsNullOrWhiteSpace(targetServiceName))
        {
            throw new InvalidOperationException("未配置 SecsListener:GrpcTargetServiceName，无法通过 Nacos 发现目标服务。");
        }

        var targetGroup = _configuration.GetValue<string>("SecsListener:GrpcTargetGroupName") ?? "DEFAULT_GROUP";
        var targetClusters = _configuration.GetSection("SecsListener:GrpcTargetClusters").Get<string[]>() ?? Array.Empty<string>();
        var targetUseHttps = _configuration.GetValue<bool>("SecsListener:GrpcTargetUseHttps", false);
        var fallbackAddresses = _configuration.GetSection("SecsListener:GrpcTargetFallbackAddresses").Get<string[]>() ?? Array.Empty<string>();

        return (targetServiceName, targetGroup, targetClusters, targetUseHttps, fallbackAddresses);
    }

    private static WaferMessage ToWaferMessage(S2F41_data data)
    {
        var message = new WaferMessage
        {
            WaferId = ReadStringParameter(data.Parameters, "WAFERID") ?? string.Empty,
            LotId = ReadStringParameter(data.Parameters, "LOTID") ?? string.Empty,
            Status = ReadStringParameter(data.Parameters, "STATUS") ?? string.Empty,
            PPID = ReadStringParameter(data.Parameters, "PPID") ?? string.Empty,
            SlotId = ReadStringParameter(data.Parameters, "SLOTID") ?? string.Empty,
            Result = ReadStringParameter(data.Parameters, "RESULT") ?? string.Empty
        };
        return message;
    }

    private static string? ReadStringParameter(Dictionary<string, Item>? parameters, params string[] candidateNames)
    {
        if (parameters is null || parameters.Count == 0)
            return null;

        foreach (var name in candidateNames)
        {
            var item = parameters.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
            var text = TryReadAsString(item);
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return null;
    }

    private static IEnumerable<uint> ReadSlotsParameter(Dictionary<string, Item>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
            yield break;

        var slotsItem = parameters.FirstOrDefault(p =>
            string.Equals(p.Key, "SLOTSLIST", StringComparison.OrdinalIgnoreCase)
            || string.Equals(p.Key, "SLOTS", StringComparison.OrdinalIgnoreCase)).Value;

        if (slotsItem is not null)
        {
            foreach (var slot in ReadSlots(slotsItem))
                yield return slot;
            yield break;
        }

        var slotIdItem = parameters.FirstOrDefault(p => string.Equals(p.Key, "SLOTID", StringComparison.OrdinalIgnoreCase)).Value;
        if (slotIdItem is not null)
        {
            foreach (var slot in ReadSlots(slotIdItem))
                yield return slot;
        }
    }

    private static string? TryReadAsString(Item? item)
    {
        if (item is null) return null;

        if (item.Format == SecsFormat.ASCII)
            return item.GetString();

        return item.Format switch
        {
            SecsFormat.U1 => item.FirstValueOrDefault<byte>(0).ToString(),
            SecsFormat.U2 => item.FirstValueOrDefault<ushort>(0).ToString(),
            SecsFormat.U4 => item.FirstValueOrDefault<uint>(0).ToString(),
            SecsFormat.I1 => item.FirstValueOrDefault<sbyte>(0).ToString(),
            SecsFormat.I2 => item.FirstValueOrDefault<short>(0).ToString(),
            SecsFormat.I4 => item.FirstValueOrDefault<int>(0).ToString(),
            _ => null
        };
    }

    private static IEnumerable<uint> ReadSlots(Item item)
    {
        if (item.Format == SecsFormat.List)
        {
            foreach (var child in item.Items)
            {
                if (TryReadUInt(child, out var slot))
                    yield return slot;
            }
            yield break;
        }

        if (item.Format == SecsFormat.ASCII)
        {
            var raw = item.GetString() ?? string.Empty;
            foreach (var token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (uint.TryParse(token, out var slot))
                    yield return slot;
            }
            yield break;
        }

        if (TryReadUInt(item, out var singleSlot))
            yield return singleSlot;
    }

    private static bool TryReadUInt(Item item, out uint value)
    {
        value = 0;

        switch (item.Format)
        {
            case SecsFormat.U1:
                value = item.FirstValueOrDefault<byte>(0);
                return true;
            case SecsFormat.U2:
                value = item.FirstValueOrDefault<ushort>(0);
                return true;
            case SecsFormat.U4:
                value = item.FirstValueOrDefault<uint>(0);
                return true;
            case SecsFormat.I1:
                {
                    var v = item.FirstValueOrDefault<sbyte>(0);
                    if (v < 0) return false;
                    value = (uint)v;
                    return true;
                }
            case SecsFormat.I2:
                {
                    var v = item.FirstValueOrDefault<short>(0);
                    if (v < 0) return false;
                    value = (uint)v;
                    return true;
                }
            case SecsFormat.I4:
                {
                    var v = item.FirstValueOrDefault<int>(0);
                    if (v < 0) return false;
                    value = (uint)v;
                    return true;
                }
            default:
                return false;
        }
    }
}
