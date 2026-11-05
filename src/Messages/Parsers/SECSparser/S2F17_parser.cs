using Secs4Net;

namespace SECSparser
{
    /// <summary>
    /// S2F17 Date and Time Request：标准请求仅包含消息头，无数据体。
    /// </summary>
    public static class S2F17_parser
    {
        /// <summary>
        /// 校验日期时间查询的 Stream/Function 及无数据体结构；W-bit 由应答层处理。
        /// </summary>
        public static void Parse(SecsMessage msg)
        {
            ArgumentNullException.ThrowIfNull(msg);

            // 防止其他消息误进入日期时间查询流程。
            if (msg.S != 2 || msg.F != 17)
                throw new ArgumentException($"Invalid message type. Expected S2F17, but got S{msg.S}F{msg.F}.");

            // Header Only 不等于 L[0]，任何数据项都属于非法数据。
            if (msg.SecsItem is not null)
                throw new InvalidOperationException("Invalid S2F17: the request must not contain a data item.");
        }
    }
}
