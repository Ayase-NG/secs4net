using Secs4Net;

namespace SECShandler.Interfaces
{
    public interface IPrimaryMessageHandler
    {
        IEnumerable<(int S, int F)> SupportedMessages { get; }

        bool CanHandle(int s, int f);

        Task HandleAsync(SecsGem secsGem, PrimaryMessageWrapper primaryMessage, CancellationToken cancellationToken);
    }
}
