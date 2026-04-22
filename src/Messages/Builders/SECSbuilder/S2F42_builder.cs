using Secs4Net;
using System;
using System.Collections.Generic;
using static Secs4Net.Item;
using SECSdata;

namespace SECSbuilder
{
    /// <summary>
    /// S2F42 "Remote Command Acknowledge" 消息的构建器。
    /// 设备通过此消息回复主机的 S2F41 远程命令。
    /// </summary>
    public static class S2F42_builder
    {
        /// <summary>
        /// 根据确认码和可选参数确认列表构建 S2F42 消息。
        /// </summary>
        /// <param name="hcack">命令确认码 (HCACK)，Binary 1 byte。</param>
        /// <param name="parameterAcks">参数确认列表，Key=CPNAME，Value=CPACK。</param>
        public static SecsMessage Build(byte hcack, IReadOnlyDictionary<string, byte>? parameterAcks = null)
        {
            var ackItems = new List<Item>();
            if (parameterAcks != null)
            {
                foreach (var kv in parameterAcks)
                {
                    ackItems.Add(L(A(kv.Key), B(kv.Value)));
                }
            }

            return new SecsMessage(2, 42, replyExpected: false)
            {
                Name = "RemoteCommandAcknowledge",
                SecsItem = L(
                    B(hcack),
                    L(ackItems.ToArray())
                )
            };
        }

        /// <summary>
        /// 向后兼容：仅传入一个错误参数名时，默认 CPACK=1。
        /// </summary>
        public static SecsMessage Build(byte hcack, string? errorParam)
        {
            if (string.IsNullOrWhiteSpace(errorParam))
            {
                return Build(hcack, (IReadOnlyDictionary<string, byte>?)null);
            }

            return Build(hcack, new Dictionary<string, byte>
            {
                [errorParam] = 1
            });
        }

        /// <summary>
        /// 通过 S2F42_data 数据对象构建消息。
        /// </summary>
        public static SecsMessage Build(S2F42_data data)
        {
            ArgumentNullException.ThrowIfNull(data);
            return Build(data.HCACK, data.ParameterAcks);
        }
    }
}
