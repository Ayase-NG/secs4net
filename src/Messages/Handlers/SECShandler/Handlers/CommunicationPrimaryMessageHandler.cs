using Secs4Net;
using SECShandler.Functions;
using SECShandler.Interfaces;

namespace SECShandler.Handlers
{
    public sealed class CommunicationPrimaryMessageHandler : IPrimaryMessageHandler
    {
        private readonly IDevice _device;

        public CommunicationPrimaryMessageHandler(IDevice device)
        {
            _device = device;
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
                    await CommunicationSxFyFunctions.HandleS1F17ReplyAsync(secsGem, _device, primaryMessage);
                    break;
            }
        }
    }
}
