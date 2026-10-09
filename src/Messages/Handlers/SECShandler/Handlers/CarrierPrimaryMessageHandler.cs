using Secs4Net;
using SECShandler.Functions;
using SECShandler.Interfaces;

namespace SECShandler.Handlers
{
    /// <summary>
    /// 载具操作相关 PrimaryMessage 处理器。
    /// 当前负责处理 S3F17。
    /// </summary>
    public sealed class CarrierPrimaryMessageHandler : IPrimaryMessageHandler
    {
        private readonly ICommandDispatcher _commandDispatcher;
        // 本处理器负责处理 S3F17 载具操作请求，注册进 SupportedMessages。
        public IEnumerable<(int S, int F)> SupportedMessages =>
        [
            (3, 17)
        ];

        public bool CanHandle(int s, int f) => (s, f) == (3, 17);

        public async Task HandleAsync(SecsGem secsGem, PrimaryMessageWrapper primaryMessage, CancellationToken cancellationToken)
        {
            // 异步调用说明：将 S3F17 分发到共享函数进行解析与应答。
            await CarrierSxFyFunctions.HandleS3F17Async(primaryMessage, _commandDispatcher, cancellationToken);
        }
    }
}
