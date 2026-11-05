using Secs4Net;
using SECSbuilder;
using SECShandler.Interfaces;
using SECSparser;

namespace SECShandler.Functions
{
    /// <summary>
    /// 时间同步相关 SxFy 处理函数。
    /// </summary>
    public static class TimeSyncSxFyFunctions
    {
        /// <summary>
        /// 校验 S2F17 并按 W-bit 返回 S2F18；非法数据体返回 S9F7，不修改设备时间。
        /// </summary>
        public static async Task HandleS2F17ReplyAsync(
            PrimaryMessageWrapper primary,
            ITimeSyncStorage timeSyncStorage,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(primary);
            ArgumentNullException.ThrowIfNull(timeSyncStorage);
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // 查询请求必须是 S2F17 Header Only，而不是空列表。
                S2F17_parser.Parse(primary.PrimaryMessage);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                // 包装器只支持应答有 W-bit 的主消息，非法请求不能伪装成正常时间应答。
                if (primary.PrimaryMessage.ReplyExpected)
                {
                    // 异步发送 S9F7 Illegal Data，包装器自动携带原请求消息头。
                    await primary.TryReplyAsync(null, cancellationToken).ConfigureAwait(false);
                }

                return;
            }

            // 无 W-bit 时不发送 SecondaryMessage，查询不会产生设置时间的副作用。
            if (!primary.PrimaryMessage.ReplyExpected)
                return;

            var reply = S2F18_builder.Build(timeSyncStorage.GetEquipmentTimeUtc());
            // 异步返回设备当前时间，沿用原请求的 System Bytes 关联事务。
            await primary.TryReplyAsync(reply, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 执行 S2F31 时间设置，仅在主消息要求应答时返回 S2F32。
        /// </summary>
        public static async Task HandleS2F31ReplyAsync(
            PrimaryMessageWrapper primary,
            ITimeSyncStorage timeSyncStorage,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(primary);
            ArgumentNullException.ThrowIfNull(timeSyncStorage);
            cancellationToken.ThrowIfCancellationRequested();

            byte tiack = 0;

            try
            {
                // W-bit 只控制是否应答，不影响 TIME 校验和设备时间设置。
                var request = S2F31_parser.Parse(primary.PrimaryMessage);
                timeSyncStorage.UpdateHostTime(request.HostTimeUtc, request.TimeText);
            }
            catch (OperationCanceledException)
            {
                // 取消不属于设置失败，交由调用方结束处理。
                throw;
            }
            catch
            {
                // 无效 TIME 或设备设置失败统一返回未完成，不能使用保留值 2。
                tiack = 1;
            }

            // 无 W-bit 时已完成请求处理，但不能发送 SecondaryMessage。
            if (!primary.PrimaryMessage.ReplyExpected)
                return;

            // 异步发送与原请求关联的 S2F32，并响应监听服务的取消请求。
            await primary.TryReplyAsync(S2F32_builder.Build(tiack), cancellationToken).ConfigureAwait(false);
        }
    }
}
