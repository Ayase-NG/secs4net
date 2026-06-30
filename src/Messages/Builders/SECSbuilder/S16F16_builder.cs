using Secs4Net;
using SECSdata;

namespace SECSbuilder
{
    /// <summary>
    /// S16F16 请求应答消息构建器（最小实现）。
    /// 常见结构：&lt;B ACK&gt;。
    /// </summary>
    public static class S16F16_builder
    {
        /// <summary>
        /// 根据 ACK 构建 S16F16 消息。
        /// </summary>
        /// <param name="ack">确认码：0=接受，1=拒绝。</param>
        public static SecsMessage Build(byte ack)
        {
            // 构造标准 S16F16 回包。
            return new SecsMessage(16, 16, replyExpected: false)
            {
                Name = "S16F16Acknowledge",
                SecsItem = Item.B(new byte[] { ack })
            };
        }

        /// <summary>
        /// 通过数据模型构建 S16F16 消息。
        /// </summary>
        public static SecsMessage Build(S16F16_data data)
        {
            // if 关键分支：参数为空时抛出标准异常。
            ArgumentNullException.ThrowIfNull(data);
            return Build(data.ACK);
        }
    }
}
