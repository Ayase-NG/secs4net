using SECShandler.Interfaces;

namespace SECSGrpcService.Services;

/// <summary>
/// 周期性向下游 EFEM 服务拉取设备状态，并刷新本进程内 IDevice 运行态缓存。
/// </summary>
public sealed class DeviceStatusRefreshService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly SecsEfemGrpc _secsEfemGrpc;
    private readonly IDevice _device;
    private readonly ILogger<DeviceStatusRefreshService> _logger;

    public DeviceStatusRefreshService(
        IConfiguration configuration,
        SecsEfemGrpc secsEfemGrpc,
        IDevice device,
        ILogger<DeviceStatusRefreshService> logger)
    {
        _configuration = configuration;
        _secsEfemGrpc = secsEfemGrpc;
        _device = device;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var section = _configuration.GetSection("DeviceStatusRefresh");
        var enabled = section.GetValue<bool>("Enabled", true);
        if (!enabled)
        {
            _logger.LogInformation("Device status refresh is disabled.");
            return;
        }

        var intervalMs = Math.Max(1000, section.GetValue<int>("IntervalMs", 5000));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var targetServiceName = _configuration.GetValue<string>("SecsListener:GrpcTargetServiceName");
                if (string.IsNullOrWhiteSpace(targetServiceName))
                {
                    _logger.LogWarning("Skip status refresh: `SecsListener:GrpcTargetServiceName` is empty.");
                }
                else
                {
                    var targetGroup = _configuration.GetValue<string>("SecsListener:GrpcTargetGroupName") ?? "DEFAULT_GROUP";
                    var targetClusters = _configuration.GetSection("SecsListener:GrpcTargetClusters").Get<string[]>() ?? Array.Empty<string>();
                    var targetUseHttps = _configuration.GetValue<bool>("SecsListener:GrpcTargetUseHttps", false);
                    var fallbackAddresses = _configuration.GetSection("SecsListener:GrpcTargetFallbackAddresses").Get<string[]>() ?? Array.Empty<string>();

                    var status = await _secsEfemGrpc.GetStatusFromServiceAsync(
                        targetServiceName,
                        targetGroup,
                        targetClusters,
                        targetUseHttps,
                        fallbackAddresses,
                        cancellationToken: stoppingToken);

                    if (status is not null)
                    {
                        // if 关键分支：当前最小实现中，状态拉取成功即视为 On-Line Remote。
                        _device.IsOnline = status.MessageCode == 0 ? DeviceOnlineState.OnLineRemote : DeviceOnlineState.OffLine;
                        _device.Mode = string.IsNullOrWhiteSpace(status.Mode) ? _device.Mode : status.Mode;
                        _device.Status = status.RunStatus ?? string.Empty;
                        _device.RunStatus = ParseRunStatus(status.RunStatus);
                    }
                    else
                    {
                        _device.IsOnline = DeviceOnlineState.OffLine;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _device.IsOnline = DeviceOnlineState.OffLine;
                _logger.LogWarning(ex, "Device status refresh failed.");
            }

            await Task.Delay(intervalMs, stoppingToken);
        }
    }

    private static DeviceRunStatus ParseRunStatus(string? runStatus)
        => (runStatus ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "unknown" => DeviceRunStatus.Unknown,
            "initializing" => DeviceRunStatus.Initializing,
            "idle" => DeviceRunStatus.Idel,
            "idel" => DeviceRunStatus.Idel,
            "running" => DeviceRunStatus.Running,
            "jam" => DeviceRunStatus.Jam,
            _ => DeviceRunStatus.Unknown
        };
}
