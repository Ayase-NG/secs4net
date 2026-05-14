using System;

namespace SECSdata
{
    /// <summary>
    /// S1F16 "OFF-LINE Acknowledge" 消息数据模型。
    /// 常见结构：&lt;B OFLACK&gt;。
    /// </summary>
    public class S1F16_data
    {
        /// <summary>
        /// 脱机确认码（OFLACK）。
        /// 约定：0=接受，1=拒绝。
        /// </summary>
        public byte OFLACK { get; set; }

        /// <summary>
        /// 是否接受脱机请求。
        /// </summary>
        public bool IsAccepted => OFLACK == 0;

        /// <summary>
        /// 时间戳，用于内部记录发送时间和打印日志，单位为 UTC 时间。
        /// </summary>
        public DateTime timeStamp { get; set; } = DateTime.UtcNow;
    }
}
