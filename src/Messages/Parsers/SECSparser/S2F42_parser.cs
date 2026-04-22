using Secs4Net;
using SECSdata;
using System;

namespace SECSparser
{
    /// <summary>
    /// S2F42 "Remote Command Acknowledge" 消息解析器。
    /// 常见结构：L[2] { HCACK(B[1]), PARAM_ACK_LIST(L[n]{CPNAME, CPACK}) }
    /// </summary>
    public static class S2F42_parser
    {
        public static S2F42_data Parse(SecsMessage msg)
        {
            if (msg.S != 2 || msg.F != 42)
                throw new ArgumentException($"Invalid message type. Expected S2F42, but got S{msg.S}F{msg.F}.");

            var root = msg.SecsItem;
            if (root == null || root.Format != SecsFormat.List || root.Count < 2)
                throw new InvalidOperationException("Invalid S2F42 message: root list missing or invalid.");

            var data = new S2F42_data();

            // HCACK
            var hcackItem = root[0];
            if (hcackItem == null)
                throw new InvalidOperationException("Invalid S2F42 message: HCACK not found.");

            data.HCACK = hcackItem.Format switch
            {
                SecsFormat.Binary => hcackItem.FirstValueOrDefault<byte>(0),
                SecsFormat.U1 => hcackItem.FirstValueOrDefault<byte>(0),
                _ => throw new InvalidOperationException("Invalid S2F42 message: HCACK type error. Expected B/U1.")
            };

            // 参数确认列表（标准）
            var second = root[1];
            if (second.Format == SecsFormat.List)
            {
                foreach (var ackItem in second.Items)
                {
                    if (ackItem.Format != SecsFormat.List || ackItem.Count < 2)
                        continue;

                    var cpNameItem = ackItem[0];
                    var cpAckItem = ackItem[1];

                    if (cpNameItem == null || cpNameItem.Format != SecsFormat.ASCII || cpAckItem == null)
                        continue;

                    byte cpAck = cpAckItem.Format switch
                    {
                        SecsFormat.Binary => cpAckItem.FirstValueOrDefault<byte>(0),
                        SecsFormat.U1 => cpAckItem.FirstValueOrDefault<byte>(0),
                        _ => 1
                    };

                    var cpName = cpNameItem.GetString();
                    if (!string.IsNullOrWhiteSpace(cpName))
                        data.ParameterAcks[cpName] = cpAck;
                }
            }
            else if (second.Format == SecsFormat.ASCII)
            {
                // 向后兼容：历史实现可能把第二个元素作为单个字符串
                var err = second.GetString();
                if (!string.IsNullOrWhiteSpace(err))
                    data.ParameterAcks[err] = 1;
            }

            data.timeStamp = DateTime.UtcNow;
            return data;
        }
    }
}
