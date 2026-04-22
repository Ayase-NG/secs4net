using Secs4Net;

namespace SECSGrpcService.Services.PrimaryMessageHandlers;

public sealed class CommunicationPrimaryMessageHandler : IPrimaryMessageHandler
{
    private readonly ILogger<CommunicationPrimaryMessageHandler> _logger;

    public CommunicationPrimaryMessageHandler(ILogger<CommunicationPrimaryMessageHandler> logger)
    {
        _logger = logger;
    }

    public bool CanHandle(int s, int f) => (s, f) is (1, 1) or (1, 13);

    public Task HandleAsync(PrimaryMessageWrapper primaryMessage, CancellationToken cancellationToken)
    {
        var msg = primaryMessage.PrimaryMessage;

        switch ((msg.S, msg.F))
        {
            case (1, 1):
                _logger.LogInformation("进入S1F1分发骨架。");
                Console.WriteLine("进入S1F1分发骨架");
                break;
            case (1, 13):
                _logger.LogInformation("进入S1F13分发骨架。");
                Console.WriteLine("进入S1F13分发骨架");
                break;
        }

        return Task.CompletedTask;
    }
}
