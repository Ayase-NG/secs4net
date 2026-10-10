using Secs4Net;
using static Secs4Net.Item;

namespace SECSbuilder
{
    /// <summary>
    /// S2F50 "Enhanced Remote Command Acknowledge" 消息构建器。
    /// 结构：L[2] { HCACK(B[1]), L[n] { L[2] { CPNAME(A), CPACK(B[1]) } } }。
    /// </summary>
    public static class S2F50_builder
    {
        /// <summary>
        /// 构建 S2F49 对应的 S2F50 应答，参数确认列表可为空。
        /// </summary>
        /// <param name="hcack">命令确认码（HCACK），Binary 单字节。</param>
        /// <param name="parameterAcks">参数名称及对应的确认码（CPACK）。</param>
        /// <returns>不要求二次回复的 S2F50 消息。</returns>
        public static SecsMessage Build(byte hcack, IReadOnlyDictionary<string, byte>? parameterAcks = null)
        {
            var ackItems = new List<Item>();

            // 只有存在参数确认结果时才生成 L[2] { CPNAME, CPACK }。
            if (parameterAcks is not null)
            {
                foreach (var (cpName, cpAck) in parameterAcks)
                {
                    ackItems.Add(L(A(cpName), B(cpAck)));
                }
            }

            return new SecsMessage(2, 50, replyExpected: false)
            {
                Name = "EnhancedRemoteCommandAcknowledge",
                SecsItem = L(
                    B(hcack),
                    L(ackItems.ToArray()))
            };
        }
    }
}
