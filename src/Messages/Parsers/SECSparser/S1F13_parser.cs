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
    /// S1F13 消息解析器。
    /// 标准 S1F13 是 "Establish Communications Request"，消息体应为空列表。
    /// 但某些设备实现会在 S1F13 中直接携带 MDLN 和 SOFTREV（非标准变体），
    /// 本解析器兼容两种格式。
    /// </summary>
    public static class S1F13_parser
    {
        /// <summary>
        /// 解析 S1F13 消息，返回强类型数据对象。
        /// </summary>
        /// <param name="msg">从 Secs4Net 接收到的 SecsMessage 对象。</param>
        /// <returns>解析后的 S1F13_Data 对象。</returns>
        /// <exception cref="ArgumentException">如果消息类型不是 S1F13，抛出异常。</exception>
        public static S1F13_data Parse(SecsMessage msg)
        {
            // 1. 验证消息类型必须是 S1F13
            if (msg.S != 1 || msg.F != 13)
            {
                throw new ArgumentException($"Invalid message type. Expected S1F13, but got S{msg.S}F{msg.F}.");
            }

            var data = new S1F13_data
            {
                timeStamp = DateTime.UtcNow
            };

            // 2. 获取消息体（可能为 null 或空 List）
            var root = msg.SecsItem;

            // 如果没有消息体或不是 List，视为标准空消息，直接返回
            if (root == null || root.Format != SecsFormat.List)
                return data;

            // 3. 如果 List 包含至少 2 个元素，则尝试提取 MDLN 和 SOFTREV（非标准变体）
            if (root.Count >= 2)
            {
                var mdlnItem = root[0];
                var softrevItem = root[1];

                if (mdlnItem?.Format == SecsFormat.ASCII)
                    data.MDLN = mdlnItem.GetString();
                if (softrevItem?.Format == SecsFormat.ASCII)
                    data.SOFTREV = softrevItem.GetString();
            }

            return data;
        }
    }
}
