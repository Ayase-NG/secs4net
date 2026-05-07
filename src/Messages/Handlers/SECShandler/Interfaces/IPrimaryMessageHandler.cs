using Secs4Net;

namespace SECShandler.Interfaces
{
    public interface IPrimaryMessageHandler
    {
        IEnumerable<(int S, int F)> SupportedMessages { get; }

        bool CanHandle(int s, int f);

        Task HandleAsync(SecsGem secsGem, PrimaryMessageWrapper primaryMessage, CancellationToken cancellationToken);
    }

    /// <summary>
    /// SECS 交互追溯持久化接口。
    /// </summary>
    public interface ISecsInteractionHistoryStore
    {
        Task SaveInteractionAsync(string sxFy, string secsMessage, byte? hcack, DateTime createdAtUtc, CancellationToken cancellationToken);
    }
}
