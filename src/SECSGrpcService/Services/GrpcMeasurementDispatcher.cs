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

    public GrpcMeasurementDispatcher(
        IConfiguration configuration,
        SecsEfemGrpc secsEfemGrpc,
        IDevice device,
        ILogger<GrpcMeasurementDispatcher> logger)
    {
        _configuration = configuration;
        _secsEfemGrpc = secsEfemGrpc;
        _device = device;
        _logger = logger;
    }

    public async Task DispatchStartMeasurementAsync(S2F41_data data, CancellationToken cancellationToken)
    {
        var (targetServiceName, targetGroup, targetClusters, targetUseHttps, fallbackAddresses) = GetTargetOptions();
        var wafer = ToWaferMessage(data);

        _logger.LogInformation("S2F41 START -> StartMeasurement trigger. LotId={LotId}, Mode={Mode}", wafer.LotId, _device.Mode);
        Console.WriteLine($"S2F41 START 触发 gRPC StartMeasurement, LotId:{wafer.LotId}, Mode:{_device.Mode}");

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
        Console.WriteLine($"S2F41 STOP 触发 gRPC StopMeasurement, LotId:{wafer.LotId}, SlotId:{wafer.SlotId}");

        await _secsEfemGrpc.SendStopMeasurementToServiceAsync(targetServiceName, targetGroup, targetClusters, targetUseHttps, wafer, fallbackAddresses, cancellationToken);
    }

    public async Task DispatchPauseMeasurementAsync(S2F41_data data, CancellationToken cancellationToken)
    {
        var (targetServiceName, targetGroup, targetClusters, targetUseHttps, fallbackAddresses) = GetTargetOptions();
        var wafer = ToWaferMessage(data);

        _logger.LogInformation("S2F41 PAUSE -> PauseMeasurement payload. WaferId={WaferId}, SlotId={SlotId}, LotId={LotId}", wafer.WaferId, wafer.SlotId, wafer.LotId);
        Console.WriteLine($"S2F41 PAUSE 触发 gRPC PauseMeasurement，WaferId:{wafer.WaferId}, SlotId:{wafer.SlotId}, LotId:{wafer.LotId}");

        await _secsEfemGrpc.SendPauseMeasurementToServiceAsync(targetServiceName, targetGroup, targetClusters, targetUseHttps, wafer, fallbackAddresses, cancellationToken);
    }

    public async Task DispatchResumeMeasurementAsync(S2F41_data data, CancellationToken cancellationToken)
    {
        var (targetServiceName, targetGroup, targetClusters, targetUseHttps, fallbackAddresses) = GetTargetOptions();
        var wafer = ToWaferMessage(data);

        _logger.LogInformation("S2F41 RESUME -> ResumeMeasurement payload. WaferId={WaferId}, SlotId={SlotId}, LotId={LotId}", wafer.WaferId, wafer.SlotId, wafer.LotId);
        Console.WriteLine($"S2F41 RESUME 触发 gRPC ResumeMeasurement，WaferId:{wafer.WaferId}, SlotId:{wafer.SlotId}, LotId:{wafer.LotId}");

        await _secsEfemGrpc.SendResumeMeasurementToServiceAsync(targetServiceName, targetGroup, targetClusters, targetUseHttps, wafer, fallbackAddresses, cancellationToken);
    }

    public async Task DispatchProcessProgramSelectAsync(S2F41_data data, CancellationToken cancellationToken)
    {
        var (targetServiceName, targetGroup, targetClusters, targetUseHttps, fallbackAddresses) = GetTargetOptions();
        var wafer = ToWaferMessage(data);

        var slots = ReadSlotsParameter(data.Parameters).ToList();
        _device.SlotsList = slots;

        var recipe = new RecipeMessage
        {
            PPID = wafer.PPID,
            Mode = _device.Mode,
            LotId = wafer.LotId ?? string.Empty
        };
        recipe.SlotsList.Add(_device.SlotsList);

        var slotsText = recipe.SlotsList.Count > 0 ? string.Join(',', recipe.SlotsList) : "<empty>";
        _logger.LogInformation("S2F41 PPSELECT -> ProcessProgramSelect payload. PPID={PPID}, Mode={Mode}, LotId={LotId}, SlotsList={Slots}", recipe.PPID, recipe.Mode, recipe.LotId, slotsText);
        Console.WriteLine($"S2F41 PPSELECT 触发 gRPC ProcessProgramSelect，PPID:{recipe.PPID}, Mode:{recipe.Mode}, LotId:{recipe.LotId}, SlotsList:{slotsText}");

        await _secsEfemGrpc.SendProcessProgramSelectToServiceAsync(targetServiceName, targetGroup, targetClusters, targetUseHttps, recipe, fallbackAddresses, cancellationToken);
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
