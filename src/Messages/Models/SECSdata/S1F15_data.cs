using System;

namespace SECSdata
{
    /// <summary>
    /// S1F15 "Request OFF-LINE" 消息数据模型。
    /// 标准消息体通常为空（W）。
    /// </summary>
    public class S1F15_data
    {
        /// <summary>
        /// 时间戳，用于内部记录接收时间和日志追踪，单位为 UTC 时间。
        /// </summary>
        public DateTime timeStamp { get; set; } = DateTime.UtcNow;
    }
}
