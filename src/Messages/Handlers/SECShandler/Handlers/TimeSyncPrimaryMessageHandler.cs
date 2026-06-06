using Secs4Net;
using SECShandler.Functions;
using SECShandler.Interfaces;

namespace SECShandler.Handlers
{
    /// <summary>
    /// 时间同步 PrimaryMessage 处理器。
    /// </summary>
    public sealed class TimeSyncPrimaryMessageHandler : IPrimaryMessageHandler
    {
        private readonly ITimeSyncStorage _timeSyncStorage;

        public TimeSyncPrimaryMessageHandler(ITimeSyncStorage timeSyncStorage)
        {
            _timeSyncStorage = timeSyncStorage;
        }

        public IEnumerable<(int S, int F)> SupportedMessages =>
        [
            (2, 31)
        ];

        public bool CanHandle(int s, int f) => (s, f) == (2, 31);

        public async Task HandleAsync(SecsGem secsGem, PrimaryMessageWrapper primaryMessage, CancellationToken cancellationToken)
        {
            await TimeSyncSxFyFunctions.HandleS2F31ReplyAsync(primaryMessage, _timeSyncStorage).ConfigureAwait(false);
        }
    }
}
