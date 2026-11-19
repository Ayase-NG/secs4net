using Secs4Net;
using SECShandler.Functions;
using SECShandler.Interfaces;

namespace SECShandler.Handlers
{
    public sealed class RemoteCommandPrimaryMessageHandler : IPrimaryMessageHandler
    {
        private readonly ICommandDispatcher _measurementDispatcher;
        private readonly IDevice _device;
        private readonly ISecsInteractionHistoryStore _interactionHistoryStore;
        private readonly IIdempotencyGuard _idempotencyGuard;
        private readonly IPortContextStorage _portContextStorage;
        private readonly IJobPlanStorage _jobPlanStorage;
        private readonly IProcessJobExecutionCoordinator? _executionCoordinator;

        /// <summary>
        /// 初始化远程命令编排及可复用的 PJ 执行触发入口。
        /// </summary>
        public RemoteCommandPrimaryMessageHandler(
            ICommandDispatcher startMeasurementDispatcher,
            IDevice device,
            ISecsInteractionHistoryStore interactionHistoryStore,
            IIdempotencyGuard idempotencyGuard,
            IPortContextStorage portContextStorage,
            IJobPlanStorage jobPlanStorage,
            IProcessJobExecutionCoordinator? executionCoordinator = null)
        {
            _measurementDispatcher = startMeasurementDispatcher;
            _device = device;
            _interactionHistoryStore = interactionHistoryStore;
            _idempotencyGuard = idempotencyGuard;
            _portContextStorage = portContextStorage;
            _jobPlanStorage = jobPlanStorage;
            _executionCoordinator = executionCoordinator;
        }

        // 本处理器负责处理 S2F41（Remote Command Request）、S16F15 与 S14F9 请求，注册进 SupportedMessages。
        public IEnumerable<(int S, int F)> SupportedMessages =>
        [
            (2, 41),
            (2, 49),
            (16, 15),
            (14, 9)
        ];

        public bool CanHandle(int s, int f) => (s, f) is (2, 41) or (2, 49) or (16, 15) or (14, 9);

        /// <summary>
        /// 分发远程命令和作业创建消息，创建确认后通过共享执行入口尝试自动加工。
        /// </summary>
        public async Task HandleAsync(SecsGem secsGem, PrimaryMessageWrapper primaryMessage, CancellationToken cancellationToken)
        {
            var msg = primaryMessage.PrimaryMessage;

            switch ((msg.S, msg.F))
            {
                case (2, 41):
                    Console.WriteLine("进入S2F41分发处理");
                    // 异步处理远程命令并返回确认。
                    await RemoteCommandSxFyFunctions.HandleS2F41Async(primaryMessage, _device, _measurementDispatcher, _interactionHistoryStore, _idempotencyGuard, cancellationToken);
                    break;
                case (2, 49):
                    Console.WriteLine("进入S2F49分发处理");
                    // 异步处理增强远程命令并返回确认。
                    await RemoteCommandSxFyFunctions.HandleS2F49Async(primaryMessage, _device, _measurementDispatcher, _interactionHistoryStore, _idempotencyGuard, cancellationToken);
                    break;
                case (16, 15):
                    Console.WriteLine("进入S16F15分发处理");
                    // 异步创建 PJ、返回结构化 S16F16，并保存设备确认结果。
                    await RemoteCommandSxFyFunctions.HandleS16F15Async(secsGem, primaryMessage, _device, _measurementDispatcher,
                        _idempotencyGuard, _portContextStorage, _jobPlanStorage, cancellationToken,
                        interactionHistoryStore: _interactionHistoryStore, executionCoordinator: _executionCoordinator);
                    break;
                case (14, 9):
                    Console.WriteLine("进入S14F9分发处理");
                    // 异步创建关联 CJ 并返回对象创建确认。
                    await RemoteCommandSxFyFunctions.HandleS14F9Async(secsGem, primaryMessage, _device, _idempotencyGuard, _portContextStorage, _jobPlanStorage, cancellationToken);
                    break;
            }
        }
    }
}
