using Secs4Net;

namespace SECSGrpcService.Services.PrimaryMessageHandlers;

public interface IPrimaryMessageHandler
{
    bool CanHandle(int s, int f);

    Task HandleAsync(PrimaryMessageWrapper primaryMessage, CancellationToken cancellationToken);
}
