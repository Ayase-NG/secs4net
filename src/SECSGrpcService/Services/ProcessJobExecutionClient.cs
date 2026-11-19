using Grpc.Net.Client;
using GY.EFEM;
using SECShandler.Interfaces;

namespace SECSGrpcService.Services;

/// <summary>
/// PJ 执行专用的严格 gRPC 客户端，固定单实例、检查返回码，不沿用广播和吞异常行为。
/// </summary>
public sealed class ProcessJobExecutionClient : IProcessJobExecutionClient
{
    private readonly NacosGrpcResolver _resolver;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ProcessJobExecutionClient> _logger;

    /// <summary>
    /// 复用现有 Nacos 及目标配置，不改变原 S2F41 命令客户端的兼容行为。
    /// </summary>
    public ProcessJobExecutionClient(NacosGrpcResolver resolver, IConfiguration configuration, ILogger<ProcessJobExecutionClient> logger)
    {
        _resolver = resolver;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// 同一目标依次执行 RecipeSelect 和 StartMeasurement；仅两次 MessageCode=0 才确认成功。
    /// </summary>
    public async Task SelectRecipeAndStartAsync(ProcessJobExecutionRequest request, CancellationToken cancellationToken)
    {
        var service = _configuration.GetValue<string>("SecsListener:GrpcTargetServiceName");
        if (string.IsNullOrWhiteSpace(service))
            throw new InvalidOperationException("SecsListener:GrpcTargetServiceName is not configured.");
        // 异步解析一次地址；本次 RecipeSelect 与 START 固定同一实例，不重试不广播。
        var addresses = await _resolver.ResolveAddressesAsync(service,
            _configuration.GetValue<string>("SecsListener:GrpcTargetGroupName") ?? "DEFAULT_GROUP",
            _configuration.GetSection("SecsListener:GrpcTargetClusters").Get<string[]>(),
            _configuration.GetValue<bool>("SecsListener:GrpcTargetUseHttps"),
            _configuration.GetSection("SecsListener:GrpcTargetFallbackAddresses").Get<string[]>(), cancellationToken).ConfigureAwait(false);
        var address = addresses.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? throw new InvalidOperationException("No EFEM endpoint is available for PJ execution.");
        using var channel = GrpcChannel.ForAddress(address);
        var client = new EFEM.EFEMClient(channel.CreateCallInvoker());
        var recipe = new RecipeMessage
        {
            PPID = request.RecipeId, PortId = request.PortId, Mode = request.Mode, LotId = request.LotId
        };
        recipe.SlotsList.Add(request.Slots);
        var timeoutMs = Math.Max(100, _configuration.GetValue<int>("SecsDispatch:Grpc:CallTimeoutMs", 5000));
        _logger.LogInformation("PJ {PJID}, Carrier {CarrierID}: RecipeSelect/StartMeasurement -> {Address}, Port={Port}, Recipe={Recipe}",
            request.PJID, request.CarrierId, address, request.PortId, request.RecipeId);
        // 异步执行可独立测试的严格顺序调用，两步共用当前客户端及目标地址。
        await ProcessJobGrpcSequence.ExecuteAsync(client, recipe, timeoutMs, cancellationToken).ConfigureAwait(false);
    }
}
