using System;

namespace SECSdata
{
    /// <summary>
    /// S3F18 "Carrier Action Acknowledge" 数据模型（用于 S3F17 的回复）。
    /// 约定：B [ACK]，0=接受，1=拒绝。
    /// </summary>
    public class S3F18_data
    {
        /// <summary>
        /// ACK 字段：0=接受，1=拒绝。
        /// </summary>
        public byte ACK { get; set; }

        /// <summary>
        /// 是否接受该动作。
        /// </summary>
        public bool IsAccepted => ACK == 0;

        /// <summary>
        /// 时间戳，用于内部记录发送时间和打印日志，单位为 UTC 时间。
        /// </summary>
        public DateTime timeStamp { get; set; } = DateTime.UtcNow;
    }
}
