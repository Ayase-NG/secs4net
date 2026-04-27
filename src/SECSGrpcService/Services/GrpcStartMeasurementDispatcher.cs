using SECShandler.Interfaces;

namespace SECSGrpcService.Services;

public sealed class GrpcMeasurementDispatcher : IMeasurementDispatcher
{
    private readonly IConfiguration _configuration;
    private readonly SecsEfemGrpc _secsEfemGrpc;
    private readonly ILogger<GrpcMeasurementDispatcher> _logger;

    public GrpcMeasurementDispatcher(
        IConfiguration configuration,
        SecsEfemGrpc secsEfemGrpc,
        ILogger<GrpcMeasurementDispatcher> logger)
    {
        _configuration = configuration;
        _secsEfemGrpc = secsEfemGrpc;
        _logger = logger;
    }

    public async Task DispatchStartMeasurementAsync(StartMeasurementDispatchRequest request, CancellationToken cancellationToken)
    {
        var (targetServiceName, targetGroup, targetClusters, targetUseHttps) = GetTargetOptions();

        var fallbackName = _configuration.GetValue<string>("SecsListener:StartName") ?? "SECS-S2F41";
        var startMessage = new StartMessage
        {
            Name = string.IsNullOrWhiteSpace(request.Name) ? fallbackName : request.Name,
            LotId = request.LotId ?? string.Empty,
            PPID = request.PPID ?? string.Empty
        };
        if (request.Slots is { Count: > 0 })
        {
            startMessage.SlotsList.AddRange(request.Slots);
        }

        var slots = startMessage.SlotsList.Count > 0 ? string.Join(",", startMessage.SlotsList) : "<empty>";
        _logger.LogInformation(
            "S2F41 START -> StartMeasurement payload. Name={Name}, LotId={LotId}, PPID={PPID}, Slots=[{Slots}]",
            startMessage.Name,
            startMessage.LotId,
            startMessage.PPID,
            slots);
        Console.WriteLine($"S2F41 START 触发 gRPC StartMeasurement，Name:{startMessage.Name}, LotId:{startMessage.LotId}, PPID:{startMessage.PPID}, Slots:[{slots}]");

        await _secsEfemGrpc.SendStartMeasurementToServiceAsync(
            targetServiceName,
            targetGroup,
            targetClusters,
            targetUseHttps,
            startMessage,
            cancellationToken);
    }

    public async Task DispatchStopMeasurementAsync(WaferDispatchRequest request, CancellationToken cancellationToken)
    {
        var (targetServiceName, targetGroup, targetClusters, targetUseHttps) = GetTargetOptions();
        var wafer = ToWaferMessage(request);

        _logger.LogInformation("S2F41 STOP -> StopMeasurement payload. WaferId={WaferId}, SlotId={SlotId}, LotId={LotId}", wafer.WaferId, wafer.SlotId, wafer.LotId);
        Console.WriteLine($"S2F41 STOP 触发 gRPC StopMeasurement，WaferId:{wafer.WaferId}, SlotId:{wafer.SlotId}, LotId:{wafer.LotId}");

        await _secsEfemGrpc.SendStopMeasurementToServiceAsync(targetServiceName, targetGroup, targetClusters, targetUseHttps, wafer, cancellationToken);
    }

    public async Task DispatchPauseMeasurementAsync(WaferDispatchRequest request, CancellationToken cancellationToken)
    {
        var (targetServiceName, targetGroup, targetClusters, targetUseHttps) = GetTargetOptions();
        var wafer = ToWaferMessage(request);

        _logger.LogInformation("S2F41 PAUSE -> PauseMeasurement payload. WaferId={WaferId}, SlotId={SlotId}, LotId={LotId}", wafer.WaferId, wafer.SlotId, wafer.LotId);
        Console.WriteLine($"S2F41 PAUSE 触发 gRPC PauseMeasurement，WaferId:{wafer.WaferId}, SlotId:{wafer.SlotId}, LotId:{wafer.LotId}");

        await _secsEfemGrpc.SendPauseMeasurementToServiceAsync(targetServiceName, targetGroup, targetClusters, targetUseHttps, wafer, cancellationToken);
    }

    public async Task DispatchResumeMeasurementAsync(WaferDispatchRequest request, CancellationToken cancellationToken)
    {
        var (targetServiceName, targetGroup, targetClusters, targetUseHttps) = GetTargetOptions();
        var wafer = ToWaferMessage(request);

        _logger.LogInformation("S2F41 RESUME -> ResumeMeasurement payload. WaferId={WaferId}, SlotId={SlotId}, LotId={LotId}", wafer.WaferId, wafer.SlotId, wafer.LotId);
        Console.WriteLine($"S2F41 RESUME 触发 gRPC ResumeMeasurement，WaferId:{wafer.WaferId}, SlotId:{wafer.SlotId}, LotId:{wafer.LotId}");

        await _secsEfemGrpc.SendResumeMeasurementToServiceAsync(targetServiceName, targetGroup, targetClusters, targetUseHttps, wafer, cancellationToken);
    }

    public async Task DispatchProcessProgramSelectAsync(RecipeDispatchRequest request, CancellationToken cancellationToken)
    {
        var (targetServiceName, targetGroup, targetClusters, targetUseHttps) = GetTargetOptions();
        var recipe = new RecipeMessage
        {
            PPName = request.PPName ?? string.Empty,
            PPID = request.PPID ?? string.Empty,
            WaferId = request.WaferId ?? string.Empty,
            LotId = request.LotId ?? string.Empty
        };

        _logger.LogInformation("S2F41 PPSELECT -> ProcessProgramSelect payload. PPName={PPName}, PPID={PPID}, LotId={LotId}", recipe.PPName, recipe.PPID, recipe.LotId);
        Console.WriteLine($"S2F41 PPSELECT 触发 gRPC ProcessProgramSelect，PPName:{recipe.PPName}, PPID:{recipe.PPID}, LotId:{recipe.LotId}");

        await _secsEfemGrpc.SendProcessProgramSelectToServiceAsync(targetServiceName, targetGroup, targetClusters, targetUseHttps, recipe, cancellationToken);
    }

    private (string ServiceName, string GroupName, string[] Clusters, bool UseHttps) GetTargetOptions()
    {
        var targetServiceName = _configuration.GetValue<string>("SecsListener:GrpcTargetServiceName");
        if (string.IsNullOrWhiteSpace(targetServiceName))
        {
            throw new InvalidOperationException("未配置 SecsListener:GrpcTargetServiceName，无法通过 Nacos 发现目标服务。");
        }

        var targetGroup = _configuration.GetValue<string>("SecsListener:GrpcTargetGroupName") ?? "DEFAULT_GROUP";
        var targetClusters = _configuration.GetSection("SecsListener:GrpcTargetClusters").Get<string[]>() ?? Array.Empty<string>();
        var targetUseHttps = _configuration.GetValue<bool>("SecsListener:GrpcTargetUseHttps", false);

        return (targetServiceName, targetGroup, targetClusters, targetUseHttps);
    }

    private static WaferMessage ToWaferMessage(WaferDispatchRequest request)
        => new()
        {
            SlotId = request.SlotId ?? string.Empty,
            WaferId = request.WaferId ?? string.Empty,
            LotId = request.LotId ?? string.Empty,
            Status = request.Status ?? string.Empty,
            PPID = request.PPID ?? string.Empty,
            Result = request.Result ?? string.Empty
        };
}
