using System;

namespace SECSdata
{
    /// <summary>
    /// S1F18 "ON-LINE Acknowledge" 消息数据模型。
    /// 常见结构：&lt;B 0x00&gt;（ONLACK）。
    /// </summary>
    public class S1F18_data
    {
        /// <summary>
        /// 上线确认码（ONLACK）。
        /// 约定：0=接受，1=拒绝。
        /// </summary>
        public byte ONLACK { get; set; }

        /// <summary>
        /// 是否接受上线请求。
        /// </summary>
        public bool IsAccepted => ONLACK == 0;

        /// <summary>
        /// 时间戳，用于内部记录发送时间和打印日志，单位为 UTC 时间。
        /// </summary>
        public DateTime timeStamp { get; set; } = DateTime.UtcNow;
    }
}
