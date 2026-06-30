using Secs4Net;
using SECSdata;

namespace SECSbuilder
{
    /// <summary>
    /// S1F18 "ON-LINE Acknowledge" 消息构建器。
    /// 常见结构：&lt;B ONLACK&gt;。
    /// </summary>
    public static class S1F18_builder
    {
        /// <summary>
        /// 根据 ONLACK 构建 S1F18 消息。
        /// </summary>
        /// <param name="onlack">上线确认码：0=接受，1=拒绝。</param>
        /// <returns>可发送的 SecsMessage。</returns>
        public static SecsMessage Build(byte onlack)
        {
            // 构造标准 S1F18 回包。
            return new SecsMessage(1, 18, replyExpected: false)
            {
                Name = "OnLineAcknowledge",
                SecsItem = Item.B(new byte[] { onlack })
            };
        }

        /// <summary>
        /// 通过数据模型构建 S1F18 消息。
        /// </summary>
        /// <param name="data">S1F18 数据模型。</param>
        /// <returns>可发送的 SecsMessage。</returns>
        public static SecsMessage Build(S1F18_data data)
        {
            // if 关键分支：参数为空时抛出标准异常。
            ArgumentNullException.ThrowIfNull(data);
            return Build(data.ONLACK);
        }
    }
}
