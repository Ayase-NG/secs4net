using System;
using System.Collections.Generic;

namespace SECSdata
{
    /// <summary>
    /// S3F17 "Carrier Action Request"（项目内用于下发 LOTID 与 SlotMap）数据模型。
    /// 约定结构：L[2] { LOTID(A), SLOTLIST(L[n]{U4...}) }。
    /// </summary>
    public class S3F17_data
    {
        /// <summary>
        /// 批次 ID（LOTID）。
        /// </summary>
        public string LOTID { get; set; } = string.Empty;

        /// <summary>
        /// 待处理槽位列表（SlotMap）。
        /// </summary>
        public List<uint> SlotMap { get; set; } = new();

        /// <summary>
        /// 是否携带有效 LOTID。
        /// </summary>
        public bool HasLotId => !string.IsNullOrWhiteSpace(LOTID);

        /// <summary>
        /// 时间戳，用于内部记录接收时间和日志追踪，单位为 UTC 时间。
        /// </summary>
        public DateTime timeStamp { get; set; } = DateTime.UtcNow;
    }
}
