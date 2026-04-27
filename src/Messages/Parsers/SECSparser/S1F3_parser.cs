using Secs4Net;
using SECSdata;
using System;

namespace SECSparser
{
    /// <summary>
    /// S1F3 消息解析器。
    /// 常见结构：L[n] { SVID... }
    /// </summary>
    public static class S1F3_parser
    {
        /// <summary>
        /// 将原始 SecsMessage 解析为 S1F3_data 对象。
        /// </summary>
        /// <param name="msg">从 Secs4Net 接收到的 S1F3 消息。</param>
        /// <returns>解析后的强类型数据对象。</returns>
        /// <exception cref="ArgumentException">消息类型不是 S1F3 时抛出。</exception>
        /// <exception cref="InvalidOperationException">消息格式不符合预期时抛出。</exception>
        public static S1F3_data Parse(SecsMessage msg)
        {
            if (msg.S != 1 || msg.F != 3)
                throw new ArgumentException($"Invalid message type. Expected S1F3, but got S{msg.S}F{msg.F}.");

            var data = new S1F3_data
            {
                timeStamp = DateTime.UtcNow
            };

            var root = msg.SecsItem;
            if (root is null)
            {
                // 兼容空消息体：等价于空 SVID 列表
                return data;
            }

            if (root.Format != SecsFormat.List)
                throw new InvalidOperationException("Invalid S1F3 message: root item is not a List.");

            foreach (var svidItem in root.Items)
            {
                try
                {
                    uint svid;
                    if (svidItem.Format == SecsFormat.List && svidItem.Count > 0)
                        svid = svidItem[0].GetUIntId("SV");
                    else
                        svid = svidItem.GetUIntId("SV");

                    if (svid != 0)
                        data.SVIDList.Add(svid);
                }
                catch (InvalidOperationException)
                {
                    // 忽略单个格式错误的 SVID，继续解析其他项。
                    continue;
                }
            }

            return data;
        }
    }
}
