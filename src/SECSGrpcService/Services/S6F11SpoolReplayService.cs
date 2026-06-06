using SECShandler.Interfaces;

namespace SECSGrpcService.Services;

/// <summary>
/// S6F11 断线缓存补发后台服务。
/// </summary>
public sealed class S6F11SpoolReplayService : BackgroundService
{
    private readonly ILogger<S6F11SpoolReplayService> _logger;
    private readonly IActiveSxFyDispatcher _activeSxFyDispatcher;
    private readonly IS6F11SpoolStorage _s6f11SpoolStorage;
    private readonly IConfiguration _configuration;

    public S6F11SpoolReplayService(
        ILogger<S6F11SpoolReplayService> logger,
        IActiveSxFyDispatcher activeSxFyDispatcher,
        IS6F11SpoolStorage s6f11SpoolStorage,
        IConfiguration configuration)
    {
        _logger = logger;
        _activeSxFyDispatcher = activeSxFyDispatcher;
        _s6f11SpoolStorage = s6f11SpoolStorage;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var section = _configuration.GetSection("SecsDispatch:S6F11");
        var intervalMs = Math.Max(200, section.GetValue<int>("SpoolReplayIntervalMs", 1000));
        var batchSize = Math.Max(1, section.GetValue<int>("SpoolReplayBatchSize", 50));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // if 关键分支：有积压时才尝试补发。
                if (_s6f11SpoolStorage.Count > 0)
                {
                    var flushed = await _activeSxFyDispatcher
                        .FlushS6F11SpoolAsync(batchSize, stoppingToken)
                        .ConfigureAwait(false);

                    if (flushed > 0)
                    {
                        _logger.LogInformation("S6F11 spool replay flushed {Count} items. Remaining={Remaining}", flushed, _s6f11SpoolStorage.Count);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "S6F11 spool replay iteration failed.");
            }

            await Task.Delay(intervalMs, stoppingToken).ConfigureAwait(false);
        }
    }
}
