using Secs4Net;
using SECSdata;
using static Secs4Net.Item;

namespace SECSbuilder
{
    /// <summary>
    /// S5F1 Alarm Report Send (ARS) 消息构建器。
    /// Equipment 侧用于向 Host 主动上报报警。
    /// </summary>
    public static class S5F1_builder
    {
        /// <summary>
        /// 构建 S5F1 消息。
        /// </summary>
        /// <param name="alcd">报警代码，Binary 1 byte（如 0x80=Set, 0x00=Clear）。</param>
        /// <param name="alid">报警编号。</param>
        /// <param name="altx">报警文本。</param>
        /// <returns>可发送的 SecsMessage。</returns>
        public static SecsMessage Build(byte alcd, uint alid, string? altx)
        {
            return new SecsMessage(5, 1, replyExpected: true)
            {
                Name = "AlarmReportSend",
                SecsItem = L(
                    B(alcd),
                    U4(alid),
                    A(altx ?? string.Empty)
                )
            };
        }

        /// <summary>
        /// 通过数据模型构建 S5F1 消息。
        /// </summary>
        public static SecsMessage Build(S5F1_data data)
        {
            ArgumentNullException.ThrowIfNull(data);
            return Build(data.ALCD, data.ALID, data.ALTX);
        }
    }
}
