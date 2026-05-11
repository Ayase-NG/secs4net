using Nacos.V2;

namespace SECSGrpcService.Services;

/// <summary>
/// 基于 Nacos Naming 的 gRPC 目标地址解析器。
/// 优先从 Nacos 获取健康实例；若获取失败或无可用实例，则回退到配置中的默认地址。
/// </summary>
public sealed class NacosGrpcResolver
{
    private readonly INacosNamingService _nacosNamingService;
    private readonly ILogger<NacosGrpcResolver> _logger;

    /// <summary>
    /// 构造地址解析器。
    /// </summary>
    /// <param name="nacosNamingService">Nacos Naming 客户端。</param>
    /// <param name="logger">日志对象。</param>
    public NacosGrpcResolver(INacosNamingService nacosNamingService, ILogger<NacosGrpcResolver> logger)
    {
        _nacosNamingService = nacosNamingService;
        _logger = logger;
    }

    /// <summary>
    /// 解析目标 gRPC 地址列表。
    /// 解析顺序：
    /// 1) 优先 Nacos 健康实例；
    /// 2) 若无实例或查询异常，则使用 fallbackAddresses。
    /// </summary>
    /// <param name="serviceName">Nacos 服务名。</param>
    /// <param name="groupName">Nacos 分组名。</param>
    /// <param name="clusters">可选集群过滤。</param>
    /// <param name="useHttps">是否使用 HTTPS 协议。</param>
    /// <param name="fallbackAddresses">Nacos 不可用时的回退地址列表。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>可用地址列表（可能为空）。</returns>
    public async Task<IReadOnlyList<string>> ResolveAddressesAsync(
        string serviceName,
        string groupName,
        IEnumerable<string>? clusters,
        bool useHttps,
        IEnumerable<string>? fallbackAddresses = null,
        CancellationToken cancellationToken = default)
    {
        // 关键分支：服务名为空时无法查询 Nacos，直接使用回退地址。
        if (string.IsNullOrWhiteSpace(serviceName))
            return BuildFallbackAddresses(useHttps, fallbackAddresses);

        // 关键分支：预处理 cluster 过滤条件，去空、去重。
        var clusterList = clusters?.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            ?? new List<string>();

        List<Nacos.V2.Naming.Dtos.Instance>? instances = null;
        try
        {
            // 关键分支：有集群过滤时按集群查，无则查服务下所有健康实例。
            if (clusterList.Count > 0)
            {
                instances = await _nacosNamingService.SelectInstances(serviceName, groupName, clusterList, healthy: true, subscribe: false);
            }
            else
            {
                instances = await _nacosNamingService.SelectInstances(serviceName, groupName, healthy: true, subscribe: false);
            }
        }
        catch (Exception ex)
        {
            // 关键分支：Nacos 查询异常，自动回退到默认地址。
            _logger.LogWarning(ex, "Resolve Nacos instances failed. Service={Service}, Group={Group}", serviceName, groupName);
            return BuildFallbackAddresses(useHttps, fallbackAddresses);
        }

        // 关键分支：未查到健康实例时回退。
        if (instances is null || instances.Count == 0)
        {
            _logger.LogWarning("No healthy Nacos instances found. Service={Service}, Group={Group}, Clusters={Clusters}", serviceName, groupName, clusterList.Count == 0 ? "<none>" : string.Join(',', clusterList));
            return BuildFallbackAddresses(useHttps, fallbackAddresses);
        }

        var scheme = useHttps ? "https" : "http";
        var addresses = instances
            .Where(i => i is { Enabled: true, Healthy: true } && !string.IsNullOrWhiteSpace(i.Ip) && i.Port > 0)
            .Select(i => $"{scheme}://{i.Ip}:{i.Port}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 关键分支：实例存在但过滤后无可用 endpoint，回退。
        if (addresses.Count == 0)
        {
            _logger.LogWarning("Nacos instances resolved but no enabled healthy endpoint available. Service={Service}", serviceName);
            return BuildFallbackAddresses(useHttps, fallbackAddresses);
        }

        _logger.LogInformation("Resolved {Count} Nacos instances for {Service}.", addresses.Count, serviceName);
        return addresses;
    }

    /// <summary>
    /// 构建回退地址列表。
    /// 若地址未显式带协议前缀，则按 useHttps 自动补全 http/https。
    /// </summary>
    /// <param name="useHttps">是否使用 HTTPS 协议。</param>
    /// <param name="fallbackAddresses">原始回退地址配置。</param>
    /// <returns>标准化后的地址列表。</returns>
    private IReadOnlyList<string> BuildFallbackAddresses(bool useHttps, IEnumerable<string>? fallbackAddresses)
    {
        var scheme = useHttps ? "https" : "http";
        var list = fallbackAddresses?
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .Select(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || a.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? a
                : $"{scheme}://{a}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();

        // 关键分支：存在回退地址时输出提示日志，便于定位 Nacos 不可用场景。
        if (list.Count > 0)
        {
            _logger.LogWarning("Using fallback gRPC target addresses: {Addresses}", string.Join(',', list));
        }

        return list;
    }
}
