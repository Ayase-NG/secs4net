using Secs4Net;
using SECSdata;

namespace SECSparser
{
    /// <summary>
    /// S7F25 格式化配方请求解析器。
    /// </summary>
    public static class S7F25_parser
    {
        /// <summary>
        /// 解析并校验 S7F25 中 ASCII 格式的 PPID。
        /// </summary>
        public static S7F25_data Parse(SecsMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);
            if (message.S != 7 || message.F != 25)
            {
                // 消息类型不匹配时拒绝按 S7F25 继续处理。
                throw new ArgumentException("Invalid message type. Expected S7F25.", nameof(message));
            }

            Item recipeNameItem = message.SecsItem
                ?? throw new ArgumentNullException(nameof(message), "S7F25 message does not contain a PPID.");
            if (recipeNameItem.Format != SecsFormat.ASCII)
            {
                // PPID 必须使用标准规定的 ASCII 类型。
                throw new ArgumentOutOfRangeException(nameof(message), "S7F25 PPID is not of type ASCII.");
            }

            string recipeName = recipeNameItem.GetString();
            if (string.IsNullOrWhiteSpace(recipeName))
            {
                // 空 PPID 无法定位对应的格式化配方文件。
                throw new ArgumentException("S7F25 PPID must not be empty.", nameof(message));
            }

            return new S7F25_data { recipeName = recipeName };
        }
    }
}
