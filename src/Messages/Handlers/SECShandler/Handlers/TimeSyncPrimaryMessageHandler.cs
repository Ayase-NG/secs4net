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
            (2, 17),
            (2, 31)
        ];

        /// <summary>
        /// 仅接收日期时间查询和日期时间设置主消息。
        /// </summary>
        public bool CanHandle(int s, int f) => (s, f) is (2, 17) or (2, 31);

        /// <summary>
        /// 按消息类型分发时间查询或设置请求，并传递服务停止令牌。
        /// </summary>
        public async Task HandleAsync(SecsGem secsGem, PrimaryMessageWrapper primaryMessage, CancellationToken cancellationToken)
        {
            // 查询和设置分别处理，防止查询请求进入 S2F31 的设备时钟更新流程。
            switch ((primaryMessage.PrimaryMessage.S, primaryMessage.PrimaryMessage.F))
            {
                case (2, 17):
                    // 异步完成 Header Only 校验及 S2F18 时间应答。
                    await TimeSyncSxFyFunctions.HandleS2F17ReplyAsync(primaryMessage, _timeSyncStorage, cancellationToken).ConfigureAwait(false);
                    break;
                case (2, 31):
                    // 异步完成 TIME 校验、设备时间设置及 S2F32 应答。
                    await TimeSyncSxFyFunctions.HandleS2F31ReplyAsync(primaryMessage, _timeSyncStorage, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
    }
}
