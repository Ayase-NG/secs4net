using GY.PLC.Comm;
using Secs4Net;
using SECSbuilder;
using SECShandler.Functions;
using SECShandler.Interfaces;

namespace SECShandler.Handlers
{
    public sealed class EventReportPrimaryMessageHandler : IPrimaryMessageHandler
    {
        private readonly IReportStorage _reportStorage;
        private readonly IEventLinkStorage _eventLinkStorage;
        private readonly IEventEnableStorage _eventEnableStorage;
        private readonly IDevice _device;
        private readonly PlcClient? _plcClient;

        public EventReportPrimaryMessageHandler(
            IReportStorage reportStorage,
            IEventLinkStorage eventLinkStorage,
            IEventEnableStorage eventEnableStorage,
            IDevice device,
            PlcClient? plcClient = null)
        {
            _reportStorage = reportStorage;
            _eventLinkStorage = eventLinkStorage;
            _eventEnableStorage = eventEnableStorage;
            _device = device;
            _plcClient = plcClient;
        }

        // 本处理器负责处理 S2F33（Event Report Request）、S2F35（Event Report Request With Report Id）和 S2F37（Event Report Request With Link Event Id），注册进SupportedMessages。
        public IEnumerable<(int S, int F)> SupportedMessages =>
        [
            (1, 3),
            (2, 33),
            (2, 35),
            (2, 37)
        ];

        public bool CanHandle(int s, int f) => (s, f) is (1, 3) or (2, 33) or (2, 35) or (2, 37);

        public async Task HandleAsync(SecsGem secsGem, PrimaryMessageWrapper primaryMessage, CancellationToken cancellationToken)
        {
            var msg = primaryMessage.PrimaryMessage;

            // if 关键分支：设备 OffLine 时，对非通信类请求返回明确拒绝应答，避免 Host 超时。
            if (_device.IsOnline == DeviceOnlineState.OffLine && (msg.S, msg.F) is (2, 33) or (2, 35) or (2, 37))
            {
                switch ((msg.S, msg.F))
                {
                    case (2, 33):
                        // DRACK=1：内部/条件不满足，当前用于离线拒绝。
                        await primaryMessage.TryReplyAsync(S2F34_builder.Build(1));
                        return;
                    case (2, 35):
                        // LRACK=1：内部/条件不满足，当前用于离线拒绝。
                        await primaryMessage.TryReplyAsync(S2F36_builder.Build(1));
                        return;
                    case (2, 37):
                        // EAC=2：格式或内部错误，当前用于离线拒绝。
                        await primaryMessage.TryReplyAsync(S2F38_builder.Build(2));
                        return;
                }
            }

            switch ((msg.S, msg.F))
            {
                case (1,3):
                    await EventReportSxFyFunctions.HandleS1F3ReplyAsync(secsGem, _device, primaryMessage, _plcClient);
                    break;
                case (2, 33):
                    await EventReportSxFyFunctions.HandleS2F33ReplyAsync(primaryMessage, _reportStorage);
                    break;
                case (2, 35):
                    await EventReportSxFyFunctions.HandleS2F35ReplyAsync(primaryMessage, _reportStorage, _eventLinkStorage);
                    break;
                case (2, 37):
                    await EventReportSxFyFunctions.HandleS2F37ReplyAsync(primaryMessage, _eventLinkStorage, _eventEnableStorage, _reportStorage);
                    break;
            }
        }
    }
}
