using Secs4Net;
using SECSdata;
using SECShandler.Interfaces;
using SECSparser;

namespace SECShandler.Functions
{
    /// <summary>
    /// 通信类 SxFy 处理函数。
    /// </summary>
    public static class CommunicationSxFyFunctions
    {
        /// <summary>
        /// 处理 S1F1（Are You There）并回复 S1F2。
        /// </summary>
        public static async Task HandleS1F1ReplyAsync(SecsGem secsGem, IDevice device, PrimaryMessageWrapper primary)
        {
            var reply = new SecsMessage(1, 2, replyExpected: false)
            {
                Name = "OnlineData",
                SecsItem = Item.L(
                    Item.A(device.ModelNumber),
                    Item.A(device.SoftwareRevision)
                )
            };

            try
            {
                await primary.TryReplyAsync(reply).ConfigureAwait(false);
            }
            catch (Exception)
            {
                await secsGem.SendAsync(reply).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 处理 S1F13（Establish Communications Request）并回复 S1F14。
        /// 仅当以下条件同时满足时返回接受（COMMACK=0）：
        /// 1) S1F13 数据结构合法；
        /// 2) 设备在线；
        /// 3) 设备运行状态允许通信（Idel/Running）。
        /// 否则返回拒绝（COMMACK=1）。
        /// </summary>
        public static async Task HandleS1F13ReplyAsync(SecsGem secsGem, IDevice device, PrimaryMessageWrapper primary)
        {
            var payloadValid = false;
            try
            {
                // 先用解析器校验消息类型与基础结构。
                var parsed = S1F13_parser.Parse(primary.PrimaryMessage);

                // 标准 S1F13 常见为空 List；
                // 非标准变体可携带 MDLN/SOFTREV（若存在要求成对出现，且为 ASCII 非空）。
                payloadValid = IsS1F13PayloadValid(primary.PrimaryMessage, parsed);
            }
            catch
            {
                payloadValid = false;
            }

            var statusAllowed = device.RunStatus is DeviceRunStatus.Idel or DeviceRunStatus.Running;
            var accept = payloadValid && device.IsOnline && statusAllowed;

            var reply = new SecsMessage(1, 14, replyExpected: false)
            {
                Name = "EstablishCommunicationsAcknowledge",
                SecsItem = Item.L(
                    Item.B(new byte[] { accept ? (byte)0 : (byte)1 }),
                    Item.L(
                        Item.A(device.ModelNumber),
                        Item.A(device.SoftwareRevision)
                    )
                )
            };

            try
            {
                await primary.TryReplyAsync(reply).ConfigureAwait(false);
            }
            catch (Exception)
            {
                await secsGem.SendAsync(reply).ConfigureAwait(false);
            }
        }

        private static bool IsS1F13PayloadValid(SecsMessage msg, S1F13_data parsed)
        {
            var root = msg.SecsItem;

            // 标准 S1F13：空消息体或空 List，视为合法。
            if (root is null)
                return true;

            if (root.Format != SecsFormat.List)
                return false;

            if (root.Count == 0)
                return true;

            // 非标准兼容：若携带 MDLN/SOFTREV，要求至少 2 个元素且均为非空。
            if (root.Count >= 2)
            {
                return !string.IsNullOrWhiteSpace(parsed.MDLN) && !string.IsNullOrWhiteSpace(parsed.SOFTREV);
            }

            // 只有 1 个元素的 payload 视为不完整。
            return false;
        }
    }
}
