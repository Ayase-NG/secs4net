using Secs4Net;
using SECSdata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SECSparser
{
    // <summary>
    /// S1F2 消息解析器。
    /// S1F2 是 "Online Data"，通常由 Equipment 发送，作为对 S1F1 的响应。
    /// 消息格式: L <A MDLN> <A SOFTREV>
    /// </summary>
    public static class S1F2_parser
    {
        /// <summary>
        /// 将原始的 SecsMessage 解析为 S1F2_Data 对象。
        /// </summary>
        /// <param name="msg">从 Secs4Net 接收到的 SecsMessage 对象。</param>
        /// <returns>解析后的强类型数据对象。</returns>
        /// <exception cref="ArgumentException">如果消息类型不是 S1F2，抛出异常。</exception>
        /// <exception cref="InvalidOperationException">如果消息格式不符合预期，抛出异常。</exception>
        public static S1F2_data Parse(SecsMessage msg)
        {
            // 1. 验证消息头：必须是 S1F2
            if (msg.S != 1 || msg.F != 2)
            {
                throw new ArgumentException($"Invalid message type. Expected S1F2, but got S{msg.S}F{msg.F}.");
            }

            // 2. 验证消息体存在且为 List
            var root = msg.SecsItem;
            if (root == null || root.Format != SecsFormat.List)
            {
                throw new InvalidOperationException("S1F2 message body is missing or not a List.");
            }

            // 3. 期望列表至少包含 2 个元素（MDLN 和 SOFTREV）
            if (root.Count < 2)
            {
                throw new InvalidOperationException("S1F2 message should contain at least 2 items (MDLN and SOFTREV).");
            }

            // 4. 提取 MDLN 和 SOFTREV（ASCII 字符串）
            var mdlnItem = root[0];
            var softrevItem = root[1];

            string modelNumber = mdlnItem?.GetString() ?? string.Empty;
            string softwareRevision = softrevItem?.GetString() ?? string.Empty;

            // 5. 创建数据对象并返回
            return new S1F2_data
            {
                MDLN = modelNumber,
                SOFTREV = softwareRevision,
                timeStamp = DateTime.UtcNow   // 可选：记录接收时间戳
            };
        }
    }
}
