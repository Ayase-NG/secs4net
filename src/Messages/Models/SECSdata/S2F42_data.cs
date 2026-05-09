using System;
using System.Collections.Generic;

namespace SECSdata
{
    /// <summary>
    /// S2F42 Remote Command Acknowledge 数据模型。
    /// 常见结构：L[2] { HCACK(B[1]), PARAM_ACK_LIST(L[n]{CPNAME,CPACK}) }
    /// SML样例：&lt;L [2] &lt;B 0x00&gt; &lt;L [0]&gt; &gt;。
    /// </summary>
    public class S2F42_data
    {
        /// <summary>
        /// 主机命令确认码 (HCACK)，Binary 1 byte。
        /// </summary>
        public byte HCACK { get; set; }

        /// <summary>
        /// 参数确认列表。Key=CPNAME, Value=CPACK。
        /// </summary>
        public Dictionary<string, byte> ParameterAcks { get; set; } = new();

        public bool IsSuccess => HCACK == 0;

        /// <summary>
        /// 向后兼容旧字段：仅映射到首个参数名（若存在）。
        /// </summary>
        [Obsolete("Use ParameterAcks instead.")]
        public string ErrorRCMD
        {
            get
            {
                foreach (var kv in ParameterAcks)
                {
                    return kv.Key;
                }
                return string.Empty;
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                if (!ParameterAcks.ContainsKey(value))
                {
                    ParameterAcks[value] = 1;
                }
            }
        }

        /// <summary>
        /// 时间戳，用于内部记录发送时间和打印日志，单位为UTC时间。
        /// </summary>
        public DateTime timeStamp { get; set; } = DateTime.UtcNow;
    }
}
