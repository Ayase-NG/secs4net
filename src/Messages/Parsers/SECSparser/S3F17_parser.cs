using Secs4Net;
using SECSdata;

namespace SECSparser
{
    /// <summary>
    /// S3F17 消息解析器。
    /// 约定格式：L[2] { LOTID(A), SLOTLIST(L[n]{U4...}) }。
    /// </summary>
    public static class S3F17_parser
    {
        /// <summary>
        /// 将原始 SecsMessage 解析为 S3F17_data 对象。
        /// </summary>
        /// <param name="msg">从 Secs4Net 接收到的 S3F17 消息。</param>
        /// <returns>解析后的强类型数据对象。</returns>
        /// <exception cref="ArgumentException">消息类型不是 S3F17 时抛出。</exception>
        /// <exception cref="InvalidOperationException">消息体格式不符合预期时抛出。</exception>
        public static S3F17_data Parse(SecsMessage msg)
        {
            // if 关键分支：先校验消息头，必须是 S3F17。
            if (msg.S != 3 || msg.F != 17)
            {
                throw new ArgumentException($"Invalid message type. Expected S3F17, but got S{msg.S}F{msg.F}.");
            }

            var root = msg.SecsItem;

            // if 关键分支：消息体必须为根 List 且至少包含 2 项。
            if (root is null || root.Format != SecsFormat.List || root.Count < 2)
            {
                throw new InvalidOperationException("Invalid S3F17 message: root list missing or invalid.");
            }

            var data = new S3F17_data
            {
                timeStamp = DateTime.UtcNow
            };

            // if 关键分支：第 1 项按 LOTID（ASCII）解析。
            var lotIdItem = root[0];
            if (lotIdItem?.Format == SecsFormat.ASCII)
            {
                data.LOTID = lotIdItem.GetString() ?? string.Empty;
            }

            // if 关键分支：第 2 项必须是槽位列表。
            var slotListItem = root[1];
            if (slotListItem is null || slotListItem.Format != SecsFormat.List)
            {
                throw new InvalidOperationException("Invalid S3F17 message: slot list missing or invalid.");
            }

            // for each 关键分支：逐项解析 SlotMap（U1/U2/U4 均可）。
            foreach (var slotItem in slotListItem.Items)
            {
                try
                {
                    uint slot;
                    if (slotItem.Format == SecsFormat.List && slotItem.Count > 0)
                        slot = slotItem[0].GetUIntId("SLOT");
                    else
                        slot = slotItem.GetUIntId("SLOT");

                    // if 关键分支：过滤 0 槽位，保持有效数据集合。
                    if (slot != 0)
                        data.SlotMap.Add(slot);
                }
                catch
                {
                    // 兜底分支：单个槽位格式异常时忽略，继续解析其余项。
                    continue;
                }
            }

            return data;
        }
    }
}
