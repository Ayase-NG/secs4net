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
    /// S1F14 消息解析器。
    /// S1F14 是 "Establish Communications Acknowledge"，由 Equipment 发送，
    /// 作为对 Host S1F13 建连请求的回复。
    /// 标准格式: L <B COMMACK> <L <A MDLN> <A SOFTREV>>
    /// </summary>
    internal class S1F14_parser
    {
        /// <summary>
        /// 解析 S1F14 消息，返回强类型数据对象。
        /// </summary>
        /// <param name="msg">从 Secs4Net 接收到的 SecsMessage 对象。</param>
        /// <returns>解析后的 S1F14_Data 对象。</returns>
        /// <exception cref="ArgumentException">如果消息类型不是 S1F14，抛出异常。</exception>
        /// <exception cref="InvalidOperationException">如果消息格式不符合预期，抛出异常。</exception>
        public static S1F14_data Parse(SecsMessage msg)
        {
            // 1. 验证消息类型必须是 S1F14
            if (msg.S != 1 || msg.F != 14)
            {
                throw new ArgumentException($"Invalid message type. Expected S1F14, but got S{msg.S}F{msg.F}.");
            }

            // 2. 验证消息体存在且为 List
            var root = msg.SecsItem;
            if (root == null || root.Format != SecsFormat.List)
            {
                throw new InvalidOperationException("S1F14 message body is missing or not a List.");
            }

            // 3. 根列表至少应有 2 个元素：COMMACK 和 子列表
            if (root.Count < 2)
            {
                throw new InvalidOperationException("S1F14 message should contain at least 2 items (COMMACK and sublist).");
            }

            // 4. 解析 COMMACK (第一个元素，应为 Binary 或 U1)
            var commAckItem = root[0];
            byte commAck = 1; // 默认拒绝
            if (commAckItem != null && (commAckItem.Format == SecsFormat.Binary || commAckItem.Format == SecsFormat.U1))
            {
                commAck = commAckItem.FirstValueOrDefault<byte>(1);
            }
            else
            {
                // 格式不符合预期，记录警告但继续，使用默认值
                // 根据标准，COMMACK 通常为 0 表示接受，非 0 表示拒绝
            }

            // 5. 解析 MDLN 和 SOFTREV (第二个元素应为子列表)
            string modelNumber = string.Empty;
            string softwareRevision = string.Empty;

            var sublist = root[1];
            if (sublist != null && sublist.Format == SecsFormat.List && sublist.Count >= 2)
            { 
                var mdlnItem = sublist[0];
                var softrevItem = sublist[1];

                if (mdlnItem?.Format == SecsFormat.ASCII)
                    modelNumber = mdlnItem.GetString() ?? string.Empty;
                if (softrevItem?.Format == SecsFormat.ASCII)
                    softwareRevision = softrevItem.GetString() ?? string.Empty;
            }
            else
            {
                // 某些非标准实现可能没有子列表，仅包含 COMMACK
                // 此时 modelNumber 和 softwareRevision 保持为空
            }

            // 6. 创建并返回数据对象
            return new S1F14_data
            {
                COMMACK = commAck,
                MDLN = modelNumber,
                SOFTREV = softwareRevision,
                timeStamp = DateTime.UtcNow
            };
        }
    }
}
