using Secs4Net;
using SECSdata;

namespace SECSparser
{
    /// <summary>
    /// S7F19 配方目录请求解析器。
    /// </summary>
    public static class S7F19_parser
    {
        /// <summary>
        /// 校验 S7F19 消息类型及其空数据结构。
        /// </summary>
        public static S7F19_data Parse(SecsMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);
            if (message.S != 7 || message.F != 19)
            {
                // 消息类型不匹配时拒绝按 S7F19 继续处理。
                throw new ArgumentException("Invalid message type. Expected S7F19.", nameof(message));
            }

            if (message.SecsItem is not null)
            {
                // 标准 S7F19 不应携带任何数据项。
                throw new ArgumentException("S7F19 message must not contain any items.", nameof(message));
            }

            return new S7F19_data();
        }
    }
}
