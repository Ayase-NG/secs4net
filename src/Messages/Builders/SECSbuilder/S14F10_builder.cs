using Secs4Net;
using SECSdata;

namespace SECSbuilder
{
    /// <summary>
    /// S14F10 请求应答消息构建器（最小实现）。
    /// 常见结构：&lt;B ACK&gt;。
    /// </summary>
    public static class S14F10_builder
    {
        /// <summary>
        /// 根据 ACK 构建 S14F10 消息。
        /// </summary>
        /// <param name="ack">确认码：0=接受，1=拒绝。</param>
        public static SecsMessage Build(byte ack)
        {
            // 方法关键节点：构造标准 S14F10 回包。
            return new SecsMessage(14, 10, replyExpected: false)
            {
                Name = "S14F10Acknowledge",
                SecsItem = Item.B(new byte[] { ack })
            };
        }

        /// <summary>
        /// 通过数据模型构建 S14F10 消息。
        /// </summary>
        public static SecsMessage Build(S14F10_data data)
        {
            // if 关键分支：参数为空时抛出标准异常。
            ArgumentNullException.ThrowIfNull(data);
            return Build(data.ACK);
        }
    }
}
