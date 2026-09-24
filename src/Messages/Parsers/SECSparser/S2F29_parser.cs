using Secs4Net;
using Secs4Net;
using SECSdata;

namespace SECSparser
{
    /// <summary>
    /// S2F29 "Equipment Constant Namelist Request" 消息解析器。
    /// 常见结构：L[n] { ECID... }。
    /// </summary>
    public static class S2F29_parser
    {
        /// <summary>
        /// 将原始 S2F29 消息解析为设备常量 ID 请求列表。
        /// </summary>
        /// <param name="msg">从 Secs4Net 接收到的 S2F29 消息。</param>
        /// <returns>解析后的强类型数据对象。</returns>
        public static S2F29_data Parse(SecsMessage msg)
        {
            // if 关键分支：仅接受 S2F29 消息，避免错误消息进入业务处理。
            if (msg.S != 2 || msg.F != 29)
            {
                throw new ArgumentException($"Invalid message type. Expected S2F29, but got S{msg.S}F{msg.F}.");
            }

            var data = new S2F29_data
            {
                timeStamp = DateTime.UtcNow
            };

            var root = msg.SecsItem;
            // if 关键分支：空消息体等价于请求全部 ECID。
            if (root is null)
            {
                return data;
            }

            // if 关键分支：S2F29 根节点必须是 ECID 列表。
            if (root.Format != SecsFormat.List)
            {
                throw new InvalidOperationException("Invalid S2F29 message: root item is not a List.");
            }

            // for each 关键分支：逐项解析 ECID，单项异常不影响其余合法请求。
            foreach (var ecidItem in root.Items)
            {
                try
                {
                    var ecid = ecidItem.Format == SecsFormat.List && ecidItem.Count > 0
                        ? ecidItem[0].GetUIntId("ECID")
                        : ecidItem.GetUIntId("ECID");

                    if (ecid != 0)
                    {
                        data.ECIDList.Add(ecid);
                    }
                }
                catch (InvalidOperationException)
                {
                    // 兜底分支：单个 ECID 格式异常时忽略，继续解析其余项。
                    continue;
                }
            }

            return data;
        }
    }
}
