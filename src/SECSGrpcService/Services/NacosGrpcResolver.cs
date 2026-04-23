using Nacos.V2;

namespace SECSGrpcService.Services;

/// <summary>
/// 基于 Nacos Naming 的 gRPC 目标地址解析器。
/// </summary>
public sealed class NacosGrpcResolver
{
    private readonly INacosNamingService _nacosNamingService;
    private readonly ILogger<NacosGrpcResolver> _logger;

    public NacosGrpcResolver(INacosNamingService nacosNamingService, ILogger<NacosGrpcResolver> logger)
    {
        _nacosNamingService = nacosNamingService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> ResolveAddressesAsync(
        string serviceName,
        string groupName,
        IEnumerable<string>? clusters,
        bool useHttps,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            return Array.Empty<string>();

        var clusterList = clusters?.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            ?? new List<string>();

        List<Nacos.V2.Naming.Dtos.Instance> instances;
        if (clusterList.Count > 0)
        {
            instances = await _nacosNamingService.SelectInstances(serviceName, groupName, clusterList, healthy: true, subscribe: false);
        }
        else
        {
            instances = await _nacosNamingService.SelectInstances(serviceName, groupName, healthy: true, subscribe: false);
        }

        if (instances is null || instances.Count == 0)
        {
            _logger.LogWarning("No healthy Nacos instances found. Service={Service}, Group={Group}, Clusters={Clusters}", serviceName, groupName, clusterList.Count == 0 ? "<none>" : string.Join(',', clusterList));
            return Array.Empty<string>();
        }

        var scheme = useHttps ? "https" : "http";
        var addresses = instances
            .Where(i => i is { Enabled: true, Healthy: true } && !string.IsNullOrWhiteSpace(i.Ip) && i.Port > 0)
            .Select(i => $"{scheme}://{i.Ip}:{i.Port}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _logger.LogInformation("Resolved {Count} Nacos instances for {Service}.", addresses.Count, serviceName);
        return addresses;
    }
}
