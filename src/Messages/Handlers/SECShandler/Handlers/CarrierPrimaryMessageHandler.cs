using Secs4Net;
using SECShandler.Functions;
using SECShandler.Interfaces;

namespace SECShandler.Handlers
{
    /// <summary>
    /// 载具/批次相关 PrimaryMessage 处理器。
    /// 当前负责处理 S3F17。
    /// </summary>
    public sealed class CarrierPrimaryMessageHandler : IPrimaryMessageHandler
    {
        private readonly IDevice _device;
        private readonly IMeasurementDispatcher _measurementDispatcher;

        public CarrierPrimaryMessageHandler(IDevice device, IMeasurementDispatcher measurementDispatcher)
        {
            _device = device;
            _measurementDispatcher = measurementDispatcher;
        }

        // 本处理器负责处理 S3F17（LOTID + SlotMap 下发），注册进 SupportedMessages。
        public IEnumerable<(int S, int F)> SupportedMessages =>
        [
            (3, 17)
        ];

        public bool CanHandle(int s, int f) => (s, f) == (3, 17);

        public async Task HandleAsync(SecsGem secsGem, PrimaryMessageWrapper primaryMessage, CancellationToken cancellationToken)
        {
            // 将 S3F17 分发到 CarrierSxFyFunctions 进行解析与状态更新。
            await CarrierSxFyFunctions.HandleS3F17Async(primaryMessage, _device, _measurementDispatcher, cancellationToken);
        }
    }
}
