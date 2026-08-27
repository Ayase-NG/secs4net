using Secs4Net;
using SECShandler.Functions;
using SECShandler.Interfaces;

namespace SECShandler.Handlers
{
    public sealed class CommunicationPrimaryMessageHandler : IPrimaryMessageHandler
    {
        private readonly IDevice _device;
        private readonly IVidValueStorage? _vidValueStorage;

        /// <summary>
        /// 初始化通信消息处理器及其设备和 VID 状态依赖。
        /// </summary>
        public CommunicationPrimaryMessageHandler(IDevice device, IVidValueStorage? vidValueStorage = null)
        {
            _device = device;
            _vidValueStorage = vidValueStorage;
        }

        // 本处理器负责处理 S1F1（Are You There?）、S1F13（Establish Communications）、S1F15（Request OFF-LINE）和 S1F17（Request ON-LINE），注册进 SupportedMessages。
        public IEnumerable<(int S, int F)> SupportedMessages =>
        [
            (1, 1),
            (1, 13),
            (1, 15),
            (1, 17)
        ];

        public bool CanHandle(int s, int f) => (s, f) is (1, 1) or (1, 13) or (1, 15) or (1, 17);

        public async Task HandleAsync(SecsGem secsGem, PrimaryMessageWrapper primaryMessage, CancellationToken cancellationToken)
        {
            var msg = primaryMessage.PrimaryMessage;

            switch ((msg.S, msg.F))
            {
                case (1, 1):
                    await CommunicationSxFyFunctions.HandleS1F1ReplyAsync(secsGem, _device, primaryMessage);
                    break;
                case (1, 13):
                    await CommunicationSxFyFunctions.HandleS1F13ReplyAsync(secsGem, _device, primaryMessage);
                    break;
                case (1, 15):
                    await CommunicationSxFyFunctions.HandleS1F15ReplyAsync(secsGem, _device, primaryMessage);
                    break;
                case (1, 17):
                    // 异步处理上线请求，并同步更新设备在线状态 VID。
                    await CommunicationSxFyFunctions.HandleS1F17ReplyAsync(
                        secsGem,
                        _device,
                        primaryMessage,
                        _vidValueStorage);
                    break;
            }
        }
    }
}
