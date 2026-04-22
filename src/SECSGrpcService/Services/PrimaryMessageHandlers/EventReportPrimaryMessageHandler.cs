using Secs4Net;

namespace SECSGrpcService.Services.PrimaryMessageHandlers;

public sealed class EventReportPrimaryMessageHandler : IPrimaryMessageHandler
{
    private readonly ILogger<EventReportPrimaryMessageHandler> _logger;

    public EventReportPrimaryMessageHandler(ILogger<EventReportPrimaryMessageHandler> logger)
    {
        _logger = logger;
    }

    public bool CanHandle(int s, int f) => (s, f) is (2, 33) or (2, 35) or (2, 37);

    public Task HandleAsync(PrimaryMessageWrapper primaryMessage, CancellationToken cancellationToken)
    {
        var msg = primaryMessage.PrimaryMessage;

        switch ((msg.S, msg.F))
        {
            case (2, 33):
                _logger.LogInformation("进入S2F33分发骨架。");
                Console.WriteLine("进入S2F33分发骨架");
                break;
            case (2, 35):
                _logger.LogInformation("进入S2F35分发骨架。");
                Console.WriteLine("进入S2F35分发骨架");
                break;
            case (2, 37):
                _logger.LogInformation("进入S2F37分发骨架。");
                Console.WriteLine("进入S2F37分发骨架");
                break;
        }

        return Task.CompletedTask;
    }
}
