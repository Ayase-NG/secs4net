using Secs4Net;
using SECShandler.Functions;
using SECShandler.Interfaces;

namespace SECShandler.Handlers
{
    public sealed class RemoteCommandPrimaryMessageHandler : IPrimaryMessageHandler
    {
        private readonly IMeasurementDispatcher _measurementDispatcher;

        public RemoteCommandPrimaryMessageHandler(IMeasurementDispatcher startMeasurementDispatcher)
        {
            _measurementDispatcher = startMeasurementDispatcher;
        }

        public IEnumerable<(int S, int F)> SupportedMessages =>
        [
            (2, 41)
        ];

        public bool CanHandle(int s, int f) => (s, f) == (2, 41);

        public async Task HandleAsync(SecsGem secsGem, PrimaryMessageWrapper primaryMessage, CancellationToken cancellationToken)
        {
            Console.WriteLine("进入S2F41分发处理");
            await RemoteCommandSxFyFunctions.HandleS2F41Async(primaryMessage, _measurementDispatcher, cancellationToken);
        }
    }
}
