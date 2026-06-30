using Secs4Net;
using SECSdata;

namespace SECSbuilder
{
    /// <summary>
    /// S1F16 "OFF-LINE Acknowledge" 消息构建器。
    /// 常见结构：&lt;B OFLACK&gt;。
    /// </summary>
    public static class S1F16_builder
    {
        /// <summary>
        /// 根据 OFLACK 构建 S1F16 消息。
        /// </summary>
        /// <param name="oflack">脱机确认码：0=接受，1=拒绝。</param>
        /// <returns>可发送的 SecsMessage。</returns>
        public static SecsMessage Build(byte oflack)
        {
            // 构造标准 S1F16 回包。
            return new SecsMessage(1, 16, replyExpected: false)
            {
                Name = "OffLineAcknowledge",
                SecsItem = Item.B(new byte[] { oflack })
            };
        }

        /// <summary>
        /// 通过数据模型构建 S1F16 消息。
        /// </summary>
        /// <param name="data">S1F16 数据模型。</param>
        /// <returns>可发送的 SecsMessage。</returns>
        public static SecsMessage Build(S1F16_data data)
        {
            // if 关键分支：参数为空时抛出标准异常。
            ArgumentNullException.ThrowIfNull(data);
            return Build(data.OFLACK);
        }
    }
}
