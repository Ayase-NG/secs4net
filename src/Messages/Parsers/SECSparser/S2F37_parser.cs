using Secs4Net;
using SECSdata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SECSparser
{
    /// <summary>
    /// S2F37 消息解析器。
    /// </summary>
    public static class S2F37_parser
    {
        /// <summary>
        /// 将原始的 SecsMessage 解析为 S2F37_Data 对象。
        /// </summary>
        /// <param name="msg">从 Secs4Net 接收到的 S2F37 消息。</param>
        /// <returns>解析后的强类型数据对象。</returns>
        /// <exception cref="ArgumentException">消息类型不是 S2F37 时抛出。</exception>
        /// <exception cref="InvalidOperationException">消息格式不符合预期时抛出。</exception>
        public static S2F37_data Parse(SecsMessage msg)
        {
            // 1. 校验消息类型必须是 S2F37
            if (msg.S != 2 || msg.F != 37)
                throw new ArgumentException($"Invalid message type. Expected S2F37, but got S{msg.S}F{msg.F}.");

            // 2. 获取根列表，应包含 2 个元素: [DATAID, CEID列表]
            var root = msg.SecsItem;
            if (root == null || root.Format != SecsFormat.List || root.Count < 2)
                throw new InvalidOperationException("Invalid S2F37 message: root list missing or invalid.");

            var data = new S2F37_data();

            // 3. 解析 DATAID (第一个元素，类型应为 U1 或 Binary)
            var dataIdItem = root[0];
            if (dataIdItem == null || (dataIdItem.Format != SecsFormat.U1 && dataIdItem.Format != SecsFormat.Binary))
                throw new InvalidOperationException("Invalid S2F37 message: DATAID not found or invalid type.");
            data.DataId = dataIdItem.FirstValueOrDefault<byte>(0);

            // 4. 解析 CEID 列表 (第二个元素，类型为 List)
            var ceidListContainer = root[1];
            if (ceidListContainer.Format != SecsFormat.List)
                throw new InvalidOperationException("Invalid S2F37 message: CEID list not found.");

            // 5. 遍历每个 CEID 项（每个项应为 U4 类型）
            foreach (var ceidItem in ceidListContainer.Items)
            {
                if (ceidItem.Format != SecsFormat.U4)
                    continue; // 忽略非 U4 的项（严格模式下可抛出异常）
                uint ceid = ceidItem.FirstValueOrDefault<uint>(0);
                if (ceid != 0)
                    data.CeidList.Add(ceid);
            }
            data.timeStamp = DateTime.UtcNow; // 创建时就会生成时间戳，此处可以根据实际需求调整，保留则储存解析完的时间
            return data;
        }
    }
}
