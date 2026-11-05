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

        /// <summary>
        /// 将时间设置请求交给共享函数，并传递服务停止令牌。
        /// </summary>
        public async Task HandleAsync(SecsGem secsGem, PrimaryMessageWrapper primaryMessage, CancellationToken cancellationToken)
        {
            // 异步完成 TIME 校验、设备时间设置及 S2F32 应答。
            await TimeSyncSxFyFunctions.HandleS2F31ReplyAsync(primaryMessage, _timeSyncStorage, cancellationToken).ConfigureAwait(false);
        }
    }
}
