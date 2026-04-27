using System;
using System.Collections.Generic;

namespace SECSdata
{
    /// <summary>
    /// S1F3 "Selected Equipment Status Request" 消息数据模型。
    /// 常见结构：L[n] { SVID... }
    /// </summary>
    public class S1F3_data
    {
        /// <summary>
        /// 状态变量 ID 列表（SVID）。
        /// </summary>
        public List<uint> SVIDList { get; set; } = new();

        /// <summary>
        /// 是否请求全部状态变量（SVID 列表为空时）。
        /// </summary>
        public bool IsAllSvid => SVIDList.Count == 0;

        /// <summary>
        /// 时间戳，用于内部记录发送时间和打印日志，单位为 UTC 时间。
        /// </summary>
        public DateTime timeStamp { get; set; } = DateTime.UtcNow;
    }
}
