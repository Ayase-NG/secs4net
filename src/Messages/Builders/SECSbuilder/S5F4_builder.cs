using Secs4Net;
using SECSdata;

namespace SECSbuilder
{
    /// <summary>
    /// S5F4 报警使能确认消息构建器。
    /// </summary>
    public static class S5F4_builder
    {
        public static SecsMessage Build(byte ackc5)
        {
            return new SecsMessage(5, 4, replyExpected: false)
            {
                Name = "EnableDisableAlarmAcknowledge",
                SecsItem = Item.B(new byte[] { ackc5 })
            };
        }

        public static SecsMessage Build(S5F4_data data)
        {
            ArgumentNullException.ThrowIfNull(data);
            return Build(data.ACKC5);
        }
    }
}
