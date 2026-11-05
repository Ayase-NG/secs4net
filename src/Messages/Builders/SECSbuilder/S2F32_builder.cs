using Secs4Net;
using SECSdata;

namespace SECSbuilder
{
    /// <summary>
    /// S2F32 时间同步应答构建器。
    /// </summary>
    public static class S2F32_builder
    {
        /// <summary>
        /// 构建 B[1] TIACK 应答，仅允许 SECS 定义的完成/未完成值。
        /// </summary>
        public static SecsMessage Build(byte tiack)
        {
            // 其他 TIACK 值为保留值，禁止发送自定义错误码。
            if (tiack > 1)
                throw new ArgumentOutOfRangeException(nameof(tiack), tiack, "TIACK must be 0 (done) or 1 (not done).");

            return new SecsMessage(2, 32, replyExpected: false)
            {
                Name = "DateAndTimeSetAcknowledge",
                SecsItem = Item.B(new byte[] { tiack })
            };
        }

        /// <summary>
        /// 根据应答数据模型构建标准 S2F32。
        /// </summary>
        public static SecsMessage Build(S2F32_data data)
        {
            ArgumentNullException.ThrowIfNull(data);
            return Build(data.TIACK);
        }
    }
}
