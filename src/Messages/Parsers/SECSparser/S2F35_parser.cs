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
    /// S2F35 "Link Event Report" 消息解析器。
    /// 主机通过此消息将报告(RPTID)与事件(CEID)进行绑定。
    /// </summary>
    public static class S2F35_parser
    {
        /// <summary>
        /// 将原始的 SecsMessage 解析为 S2F35_data 对象（Link Event Report）。
        /// 解析 CEID 与对应的 RPTID 列表并填充到返回对象的 Links 字典中。
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

            // 3. 解析 DATAID (第一个元素)，通过外部通用工具类进行类型匹配（仅接受 U1/U2/U4）
            var data = new S2F35_data();
            var dataIdItem = root[0];
            if (dataIdItem == null)
                throw new InvalidOperationException("Invalid S2F35 message: DATAID not found.");

            // 使用通用方法尝试读取 DATAID，会在类型不匹配时抛出异常并向上传播
            uint dataIdValue = dataIdItem.GetUIntId("DATA");
            if (dataIdValue > byte.MaxValue)
                throw new InvalidOperationException("Invalid S2F35 message: DATAID out of byte range.");
            data.DATAID = (byte)dataIdValue;

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

                // 解析 CEID 列表 (第一个元素)
                var ceidValueItem = ceidItem[0];
                uint ceid = ceidValueItem.GetUIntId("CE");
                if (ceid == 0) continue;

                // 解析 RPTID 列表 (第二个元素)
                var rptIdListContainer = ceidItem[1];
                if (rptIdListContainer.Format != SecsFormat.List)
                    continue;

                var rptIds = new List<uint>();
                foreach (var rptIdItem in rptIdListContainer.Items)
                {

                        uint rptId = rptIdItem.GetUIntId("RPT");
                    if (rptId != 0)
                            rptIds.Add(rptId);
                    
                }

                // 存储链接关系，注意：如果 rptIds 为空，表示解除该 CEID 的所有链接
                data.Links[ceid] = rptIds;
            }
            data.timeStamp = DateTime.UtcNow;
            return data;
        }
    }
}
