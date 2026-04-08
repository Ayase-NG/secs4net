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
    /// S2F38 消息解析器。
    /// </summary>
    public static class S2F38_parser
    {
        /// <summary>
        /// 将原始的 SecsMessage 解析为 S2F38_Data 对象。
        /// </summary>
        /// <param name="msg">从 Secs4Net 接收到的 S2F38 消息。</param>
        /// <returns>解析后的强类型数据对象。</returns>
        /// <exception cref="ArgumentException">消息类型不是 S2F38 时抛出。</exception>
        /// <exception cref="InvalidOperationException">消息格式不符合预期时抛出。</exception>
        public static S2F38_data Parse(SecsMessage msg)
        {
            // 1. 校验消息类型必须是 S2F38
            if (msg.S != 2 || msg.F != 38)
                throw new ArgumentException($"Invalid message type. Expected S2F38, but got S{msg.S}F{msg.F}.");

            // 2. 获取消息体，应为一个 Binary 类型且至少包含 1 字节
            var body = msg.SecsItem;
            if (body == null || body.Format != SecsFormat.Binary || body.Count < 1)
                throw new InvalidOperationException("Invalid S2F38 message: missing or invalid EAC data.");

            // 3. 解析 EAC (第一个字节)
            byte eac = body.FirstValueOrDefault<byte>(0xFF); // 默认无效值

            return new S2F38_data
            {
                EAC = eac,
                timeStamp = DateTime.Now // 记录解析时间
            };
        }
    }
}
