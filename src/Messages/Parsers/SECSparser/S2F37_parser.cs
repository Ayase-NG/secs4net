using Secs4Net;
using SECSdata;
using System;

namespace SECSparser
{
    /// <summary>
    /// S2F37 消息解析器。
    /// 常见结构：L[2] { CEED, CEIDLIST }
    /// </summary>
    public static class S2F37_parser
    {
        /// <summary>
        /// 将原始的 SecsMessage 解析为 S2F37_data 对象。
        /// </summary>
        /// <param name="msg">从 Secs4Net 接收到的 S2F37 消息。</param>
        /// <returns>解析后的强类型数据对象。</returns>
        /// <exception cref="ArgumentException">消息类型不是 S2F37 时抛出。</exception>
        /// <exception cref="InvalidOperationException">消息格式不符合预期时抛出。</exception>
        public static S2F37_data Parse(SecsMessage msg)
        {
            if (msg.S != 2 || msg.F != 37)
                throw new ArgumentException($"Invalid message type. Expected S2F37, but got S{msg.S}F{msg.F}.");

            var root = msg.SecsItem;
            if (root == null || root.Format != SecsFormat.List || root.Count < 2)
                throw new InvalidOperationException("Invalid S2F37 message: root list missing or invalid.");

            var data = new S2F37_data();

            // 标准：第一个元素为 CEED
            var first = root[0];
            if (first == null)
                throw new InvalidOperationException("Invalid S2F37 message: CEED not found.");

            data.CEED = ReadByteCode(first, "CEED");

            // 向后兼容：旧逻辑使用 DataId 字段
            //data.DataId = data.CEED;

            // 第二个元素为 CEIDLIST
            var ceidListContainer = root[1];
            if (ceidListContainer == null || ceidListContainer.Format != SecsFormat.List)
                throw new InvalidOperationException("Invalid S2F37 message: CEID list not found.");

            foreach (var ceidItem in ceidListContainer.Items)
            {
                try
                {
                    uint ceid;
                    if (ceidItem.Format == SecsFormat.List && ceidItem.Count > 0)
                        ceid = ceidItem[0].GetUIntId("CE");
                    else
                        ceid = ceidItem.GetUIntId("CE");

                    if (ceid != 0)
                        data.CEIDList.Add(ceid);
                }
                catch (InvalidOperationException)
                {
                    // 忽略格式错误的单个 CEID，继续解析其它项
                    continue;
                }
            }

            data.timeStamp = DateTime.UtcNow;
            return data;
        }

        private static byte ReadByteCode(Item item, string fieldName)
        {
            return item.Format switch
            {
                SecsFormat.Binary => item.FirstValueOrDefault<byte>(0),
                SecsFormat.U1 => item.FirstValueOrDefault<byte>(0),
                _ => throw new InvalidOperationException($"Invalid S2F37 message: {fieldName} type error. Expected B/U1.")
            };
        }
    }
}
