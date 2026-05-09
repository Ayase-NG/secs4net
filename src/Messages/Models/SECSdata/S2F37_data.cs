using System;
using System.Collections.Generic;

namespace SECSdata
{
    /// <summary>
    /// S2F37 "Enable/Disable Event Report" 消息数据模型。
    /// 常见结构：L[2] { CEED, CEIDLIST }
    /// SML样例：&lt;L [2] &lt;B 0x01&gt; &lt;L [2] &lt;U4 1001&gt; &lt;U4 1002&gt; &gt; &gt;。
    /// </summary>
    public class S2F37_data
    {
        /// <summary>
        /// CEED (Enable/Disable Code)，常见取值：0=Disable, 1=Enable。
        /// </summary>
        public byte CEED { get; set; }

        /// <summary>
        /// CEID 列表。为空表示对所有事件生效。
        /// </summary>
        public List<uint> CEIDList { get; set; } = new();

        /// <summary>
        /// 是否操作所有事件 (即 CEIDList 为空)。
        /// </summary>
        public bool IsAllEvents => CEIDList.Count == 0;

        /// <summary>
        /// 向后兼容旧字段：DataId（S2F37 常见结构并不包含 DATAID）。
        /// </summary>
        [Obsolete("S2F37 commonly uses CEED + CEIDLIST; DATAID is kept only for backward compatibility.")]
        public byte DataId { get; set; }

        /// <summary>
        /// 向后兼容旧字段名。
        /// </summary>
        [Obsolete("Use CEIDList instead.")]
        public List<uint> CeidList
        {
            get => CEIDList;
            set => CEIDList = value ?? new List<uint>();
        }

        /// <summary>
        /// 时间戳，用于内部记录发送时间和打印日志，单位为UTC时间。
        /// </summary>
        public DateTime timeStamp { get; set; } = DateTime.UtcNow;
    }
}
