using Secs4Net;
using SECSdata;

namespace SECSbuilder
{
    /// <summary>
    /// S2F32 时间同步应答构建器。
    /// </summary>
    public static class S2F32_builder
    {
        public static SecsMessage Build(byte tiack)
        {
            return new SecsMessage(2, 32, replyExpected: false)
            {
                Name = "DateAndTimeSetAcknowledge",
                SecsItem = Item.B(new byte[] { tiack })
            };
        }

        public static SecsMessage Build(S2F32_data data)
        {
            ArgumentNullException.ThrowIfNull(data);
            return Build(data.TIACK);
        }
    }
}
