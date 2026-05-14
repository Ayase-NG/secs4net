using Secs4Net;
using SECSdata;

namespace SECSparser
{
    /// <summary>
    /// S1F15 消息解析器。
    /// 标准 S1F15 为 "Request OFF-LINE"，消息体通常为空或空列表。
    /// </summary>
    public static class S1F15_parser
    {
        /// <summary>
        /// 将原始 SecsMessage 解析为 S1F15_data 对象。
        /// </summary>
        /// <param name="msg">从 Secs4Net 接收到的 S1F15 消息。</param>
        /// <returns>解析后的强类型数据对象。</returns>
        /// <exception cref="ArgumentException">消息类型不是 S1F15 时抛出。</exception>
        /// <exception cref="InvalidOperationException">消息体格式不符合预期时抛出。</exception>
        public static S1F15_data Parse(SecsMessage msg)
        {
            // if 关键分支：先校验消息头，必须是 S1F15。
            if (msg.S != 1 || msg.F != 15)
            {
                throw new ArgumentException($"Invalid message type. Expected S1F15, but got S{msg.S}F{msg.F}.");
            }

            var root = msg.SecsItem;

            // if 关键分支：标准允许空消息体，直接视为合法。
            if (root is null)
            {
                return new S1F15_data
                {
                    timeStamp = DateTime.UtcNow
                };
            }

            // if 关键分支：若存在消息体，要求为 List 类型（一般为空列表）。
            if (root.Format != SecsFormat.List)
            {
                throw new InvalidOperationException("Invalid S1F15 message: body should be null or List.");
            }

            // if 关键分支：非空列表在当前实现中视为格式不符合预期。
            if (root.Count > 0)
            {
                throw new InvalidOperationException("Invalid S1F15 message: list body should be empty.");
            }

            return new S1F15_data
            {
                timeStamp = DateTime.UtcNow
            };
        }
    }
}
