using Secs4Net;
using SECShandler.Functions;
using SECShandler.Interfaces;

namespace SECShandler.Handlers
{
    public sealed class RemoteCommandPrimaryMessageHandler : IPrimaryMessageHandler
    {
        private readonly IMeasurementDispatcher _measurementDispatcher;
        private readonly IDevice _device;
        private readonly ISecsInteractionHistoryStore _interactionHistoryStore;

        public RemoteCommandPrimaryMessageHandler(
            IMeasurementDispatcher startMeasurementDispatcher,
            IDevice device,
            ISecsInteractionHistoryStore interactionHistoryStore)
        {
            _measurementDispatcher = startMeasurementDispatcher;
            _device = device;
            _interactionHistoryStore = interactionHistoryStore;
        }

        // 本处理器负责处理 S2F41（Remote Command Request），注册进SupportedMessages。
        public IEnumerable<(int S, int F)> SupportedMessages =>
        [
            (2, 41)
        ];

        public bool CanHandle(int s, int f) => (s, f) == (2, 41);

        public async Task HandleAsync(SecsGem secsGem, PrimaryMessageWrapper primaryMessage, CancellationToken cancellationToken)
        {
            Console.WriteLine("进入S2F41分发处理");
            await RemoteCommandSxFyFunctions.HandleS2F41Async(primaryMessage, _device, _measurementDispatcher, _interactionHistoryStore, cancellationToken);
        }
    }
}
