using System;

namespace SECSdata
{
    /// <summary>
    /// S5F1 Alarm Report Send (ARS) 数据模型。
    /// 标准消息体格式：L[3] { ALCD(B[1]), ALID(Ux/Ix), ALTX(A) }
    /// SML样例：&lt;L [3] &lt;B 0x80&gt; &lt;U4 1001&gt; &lt;A "OVER TEMP"&gt; &gt;。
    /// </summary>
    public class S5F1_data
    {
        /// <summary>
        /// Alarm Code，Binary 1 byte。
        /// 常见：0x80 = Set(报警发生), 0x00 = Clear(报警清除)。
        /// </summary>
        public byte ALCD { get; set; }

        /// <summary>
        /// Alarm ID，报警编号。通常使用无符号整型。停机报警为1，不停机报警为2，报警清除为0
        /// </summary>
        public uint ALID { get; set; }

        /// <summary>
        /// Alarm Text，报警文本。
        /// </summary>
        public string ALTX { get; set; } = string.Empty;

        /// <summary>
        /// 时间戳，用于内部记录发送时间和日志。
        /// </summary>
        public DateTime timeStamp { get; set; } = DateTime.UtcNow;
    }
}
