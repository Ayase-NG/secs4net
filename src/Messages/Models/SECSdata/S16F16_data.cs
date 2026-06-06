using System;

namespace SECSdata
{
    /// <summary>
    /// S16F16 请求应答数据模型（最小实现）。
    /// 常见结构：&lt;B ACK&gt;。
    /// </summary>
    public class S16F16_data
    {
        /// <summary>
        /// 确认码：0=接受，1=拒绝。
        /// </summary>
        public byte ACK { get; set; }

        /// <summary>
        /// 是否接受本次请求。
        /// </summary>
        public bool IsAccepted => ACK == 0;

        /// <summary>
        /// 时间戳，用于内部记录发送时间和打印日志，单位为 UTC 时间。
        /// </summary>
        public DateTime timeStamp { get; set; } = DateTime.UtcNow;
    }
}
