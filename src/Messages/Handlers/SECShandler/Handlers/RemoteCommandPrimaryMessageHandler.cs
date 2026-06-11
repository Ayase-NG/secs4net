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
        private readonly IIdempotencyGuard _idempotencyGuard;
        private readonly IPortContextStorage _portContextStorage;
        private readonly IJobPlanStorage _jobPlanStorage;

        public RemoteCommandPrimaryMessageHandler(
            IMeasurementDispatcher startMeasurementDispatcher,
            IDevice device,
            ISecsInteractionHistoryStore interactionHistoryStore,
            IIdempotencyGuard idempotencyGuard,
            IPortContextStorage portContextStorage,
            IJobPlanStorage jobPlanStorage)
        {
            _measurementDispatcher = startMeasurementDispatcher;
            _device = device;
            _interactionHistoryStore = interactionHistoryStore;
            _idempotencyGuard = idempotencyGuard;
            _portContextStorage = portContextStorage;
            _jobPlanStorage = jobPlanStorage;
        }

        // 本处理器负责处理 S2F41（Remote Command Request）、S16F15 与 S14F9 请求，注册进 SupportedMessages。
        public IEnumerable<(int S, int F)> SupportedMessages =>
        [
            (2, 41),
            (16, 15),
            (14, 9)
        ];

        public bool CanHandle(int s, int f) => (s, f) is (2, 41) or (16, 15) or (14, 9);

        public async Task HandleAsync(SecsGem secsGem, PrimaryMessageWrapper primaryMessage, CancellationToken cancellationToken)
        {
            var msg = primaryMessage.PrimaryMessage;

            switch ((msg.S, msg.F))
            {
                case (2, 41):
                    Console.WriteLine("进入S2F41分发处理");
                    await RemoteCommandSxFyFunctions.HandleS2F41Async(primaryMessage, _device, _measurementDispatcher, _interactionHistoryStore, _idempotencyGuard, cancellationToken);
                    break;
                case (16, 15):
                    Console.WriteLine("进入S16F15分发处理");
                    await RemoteCommandSxFyFunctions.HandleS16F15Async(secsGem, primaryMessage, _device, _measurementDispatcher, _idempotencyGuard, _portContextStorage, _jobPlanStorage, cancellationToken);
                    break;
                case (14, 9):
                    Console.WriteLine("进入S14F9分发处理");
                    await RemoteCommandSxFyFunctions.HandleS14F9Async(secsGem, primaryMessage, _device, _idempotencyGuard, _portContextStorage, _jobPlanStorage, cancellationToken);
                    break;
            }
        }
    }
}
