using Secs4Net;
using SECShandler.Functions;
using SECShandler.Interfaces;

namespace SECShandler.Handlers
{
    /// <summary>
    /// 报警相关 PrimaryMessage 处理器。
    /// </summary>
    public sealed class AlarmPrimaryMessageHandler : IPrimaryMessageHandler
    {
        private readonly IAlarmEnableStorage _alarmEnableStorage;
        private readonly IAlarmStateStorage _alarmStateStorage;

        public AlarmPrimaryMessageHandler(IAlarmEnableStorage alarmEnableStorage, IAlarmStateStorage alarmStateStorage)
        {
            _alarmEnableStorage = alarmEnableStorage;
            _alarmStateStorage = alarmStateStorage;
        }

        public IEnumerable<(int S, int F)> SupportedMessages =>
        [
            (5, 3),
            (5, 5)
        ];

        public bool CanHandle(int s, int f) => (s, f) is (5, 3) or (5, 5);

        public async Task HandleAsync(SecsGem secsGem, PrimaryMessageWrapper primaryMessage, CancellationToken cancellationToken)
        {
            var msg = primaryMessage.PrimaryMessage;

            switch ((msg.S, msg.F))
            {
                case (5, 3):
                    await AlarmSxFyFunctions.HandleS5F3ReplyAsync(primaryMessage, _alarmEnableStorage).ConfigureAwait(false);
                    break;
                case (5, 5):
                    await AlarmSxFyFunctions.HandleS5F5ReplyAsync(primaryMessage, _alarmStateStorage).ConfigureAwait(false);
                    break;
            }
        }
    }
}
