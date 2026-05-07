using Microsoft.EntityFrameworkCore;
using SECShandler.Interfaces;

namespace SECSGrpcService.Services;

public sealed class RemoteCommandAckHistoryStore : ISecsInteractionHistoryStore
{
    private readonly IDbContextFactory<TraceabilityDbContext> _dbContextFactory;
    private readonly ILogger<RemoteCommandAckHistoryStore> _logger;

    public RemoteCommandAckHistoryStore(
        IDbContextFactory<TraceabilityDbContext> dbContextFactory,
        ILogger<RemoteCommandAckHistoryStore> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public async Task SaveInteractionAsync(string sxFy, string secsMessage, byte? hcack, DateTime createdAtUtc, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var row = new SecsInteractionHistoryRecord
            {
                SxFy = sxFy ?? string.Empty,
                SecsMessage = secsMessage ?? string.Empty,
                Hcack = hcack,
                CreatedAtUtc = createdAtUtc
            };

            db.SecsInteractionHistories.Add(row);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist SECS interaction history. SxFy={SxFy}, Hcack={Hcack}", sxFy, hcack);
        }
    }
}
