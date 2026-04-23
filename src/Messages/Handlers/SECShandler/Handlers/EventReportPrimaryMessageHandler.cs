using Secs4Net;
using SECShandler.Functions;
using SECShandler.Interfaces;

namespace SECShandler.Handlers
{
    public sealed class EventReportPrimaryMessageHandler : IPrimaryMessageHandler
    {
        private readonly IReportStorage _reportStorage;
        private readonly IEventLinkStorage _eventLinkStorage;
        private readonly IEventEnableStorage _eventEnableStorage;

        public EventReportPrimaryMessageHandler(
            IReportStorage reportStorage,
            IEventLinkStorage eventLinkStorage,
            IEventEnableStorage eventEnableStorage)
        {
            _reportStorage = reportStorage;
            _eventLinkStorage = eventLinkStorage;
            _eventEnableStorage = eventEnableStorage;
        }

        public IEnumerable<(int S, int F)> SupportedMessages =>
        [
            (2, 33),
            (2, 35),
            (2, 37)
        ];

        public bool CanHandle(int s, int f) => (s, f) is (2, 33) or (2, 35) or (2, 37);

        public async Task HandleAsync(SecsGem secsGem, PrimaryMessageWrapper primaryMessage, CancellationToken cancellationToken)
        {
            var msg = primaryMessage.PrimaryMessage;

            switch ((msg.S, msg.F))
            {
                case (2, 33):
                    await EventReportSxFyFunctions.HandleS2F33ReplyAsync(primaryMessage, _reportStorage);
                    break;
                case (2, 35):
                    await EventReportSxFyFunctions.HandleS2F35ReplyAsync(primaryMessage, _reportStorage, _eventLinkStorage);
                    break;
                case (2, 37):
                    await EventReportSxFyFunctions.HandleS2F37ReplyAsync(primaryMessage, _eventLinkStorage, _eventEnableStorage);
                    break;
            }
        }
    }
}
