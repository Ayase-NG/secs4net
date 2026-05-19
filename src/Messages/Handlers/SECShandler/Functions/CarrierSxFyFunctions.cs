using Secs4Net;
using SECSdata;
using SECSbuilder;
using SECShandler.Interfaces;
using SECSparser;

namespace SECShandler.Functions
{
    /// <summary>
    /// 载具/批次相关的 SxFy 处理函数集合。
    /// 当前包含 S3F17（LOTID + SlotMap 下发）处理逻辑。
    /// </summary>
    public static class CarrierSxFyFunctions
    {
        /// <summary>
        /// 处理 S3F17 消息并将结果写入设备运行态。
        /// </summary>
        /// <param name="primary">PrimaryMessage 包装对象。</param>
        /// <param name="device">设备运行态对象。</param>
        public static async Task HandleS3F17Async(PrimaryMessageWrapper primary, IDevice device)
        {
            // 方法关键节点：先解析 S3F17 请求数据。
            S3F17_data data;
            try
            {
                data = S3F17_parser.Parse(primary.PrimaryMessage);
            }
            catch (Exception ex)
            {
                // 解析失败时，如果需要回复则回复 NAK（1），并记录日志后退出。
                Console.WriteLine($"S3F17 parse error: {ex.Message}");
                if (primary.PrimaryMessage.ReplyExpected)
                {
                    var nak = S3F18_builder.Build(1);
                    await primary.TryReplyAsync(nak, CancellationToken.None);
                }
                return;
            }

            // if 关键分支：LOTID 非空时更新当前批次 ID。
            if (data.HasLotId)
            {
                device.CurrentLotId = data.LOTID.Trim();
            }

            // if 关键分支：槽位列表非空时更新设备当前处理槽位。
            if (data.SlotMap.Count > 0)
            {
                device.SlotsList = data.SlotMap;
            }

            // 方法关键分支：如果主消息期望回复，则发送 S3F18 ACK（0 表示接受，1 表示拒绝）。
            if (primary.PrimaryMessage.ReplyExpected)
            {
                try
                {
                    var ack = S3F18_builder.Build(0);
                    await primary.TryReplyAsync(ack, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    // 在回复失败时打印日志，尝试发送 NAK 作为兜底
                    Console.WriteLine($"Failed to reply S3F18: {ex.Message}");
                    try
                    {
                        var nak = S3F18_builder.Build(1);
                        await primary.TryReplyAsync(nak, CancellationToken.None);
                    }
                    catch
                    {
                        // 最终兜底：沉默处理，避免抛出异常影响上层循环
                    }
                }
            }
        }
    }
}
