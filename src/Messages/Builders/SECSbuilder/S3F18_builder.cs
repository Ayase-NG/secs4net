using Secs4Net;

namespace SECSbuilder
{
    /// <summary>
    /// S3F18 "Carrier Action Acknowledge" 构建器（用于响应 S3F17）。
    /// 常见结构：&lt;B ACK&gt;，0=接受，1=拒绝。
    /// </summary>
    public static class S3F18_builder
    {
        /// <summary>
        /// 构建 S3F18 响应。
        /// </summary>
        /// <param name="ack">确认码：0=接受，1=拒绝。</param>
        /// <returns>可发送的 SecsMessage。</returns>
        public static SecsMessage Build(byte ack)
        {
            return new SecsMessage(3, 18, replyExpected: false)
            {
                Name = "CarrierActionAcknowledge",
                SecsItem = Item.B(new byte[] { ack })
            };
        }
    }
}
