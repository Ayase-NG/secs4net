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
    /// S2F35 消息解析器。
    /// </summary>
    public static class S2F35_parser
    {
        /// <summary>
        /// 将原始的 SecsMessage 解析为 S2F35_Data 对象。
        /// </summary>
        /// <param name="msg">从 Secs4Net 接收到的 S2F35 消息。</param>
        /// <returns>解析后的强类型数据对象。</returns>
        /// <exception cref="ArgumentException">消息类型不是 S2F35 时抛出。</exception>
        /// <exception cref="InvalidOperationException">消息格式不符合预期时抛出。</exception>
        public static S2F35_data Parse(SecsMessage msg)
        {
            // 1. 校验消息类型必须是 S2F35
            if (msg.S != 2 || msg.F != 35)
                throw new ArgumentException($"Invalid message type. Expected S2F35, but got S{msg.S}F{msg.F}.");

            // 2. 获取根列表，应包含 2 个元素: [DATAID, CEID列表]
            var root = msg.SecsItem;
            if (root == null || root.Format != SecsFormat.List || root.Count < 2)
                throw new InvalidOperationException("Invalid S2F35 message: root list missing or invalid.");

            // 3. 解析 DATAID (第一个元素，类型 U1 或 Binary)
            var data = new S2F35_data();
            var dataIdItem = root[0];
            if (dataIdItem == null || (dataIdItem.Format != SecsFormat.U1 && dataIdItem.Format  != SecsFormat.Binary))
                throw new InvalidOperationException("Invalid S2F35 message: DATAID not found or invalid type.");
            data.DataId = dataIdItem.FirstValueOrDefault<byte>(0);

            // 4. 解析 CEID 列表 (第二个元素)
            var ceidListContainer = root[1];
            if (ceidListContainer.Format != SecsFormat.List)
                throw new InvalidOperationException("Invalid S2F35 message: CEID list not found.");

            // 5. 遍历每个 CEID 项
            foreach (var ceidItem in ceidListContainer.Items)
            {
                // 每个 CEID 项应是一个包含 2 个元素的列表: [CEID, RPTID列表]
                if (ceidItem.Format != SecsFormat.List || ceidItem.Count < 2)
                    continue; // 忽略格式错误的项

                // 解析 CEID (第一个元素，类型 U4)
                var ceidValueItem = ceidItem[0];
                if (ceidValueItem == null || ceidValueItem.Format != SecsFormat.U4)
                    continue;
                uint ceid = ceidValueItem.FirstValueOrDefault<uint>(0);
                if (ceid == 0) continue;

                // 解析 RPTID 列表 (第二个元素)
                var rptIdListContainer = ceidItem[1];
                if (rptIdListContainer.Format != SecsFormat.List)
                    continue;

                var rptIds = new List<uint>();
                foreach (var rptIdItem in rptIdListContainer.Items)
                {
                    if (rptIdItem.Format == SecsFormat.U4)
                    {
                        uint rptId = rptIdItem.FirstValueOrDefault<uint>(0);
                        if (rptId != 0)
                            rptIds.Add(rptId);
                    }
                }

                // 存储链接关系，注意：如果 rptIds 为空，表示解除该 CEID 的所有链接
                data.Links[ceid] = rptIds;
            }
            data.timeStamp = DateTime.UtcNow;
            return data;
        }
    }
}
