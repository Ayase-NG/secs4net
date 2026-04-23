using System;
using System.Collections.Generic;

namespace SECSdata
{
    /// <summary>
    /// S6F11 "Event Report Send" 数据模型。
    /// 常见结构：L[3] { DATAID, CEID, REPORTS }
    /// REPORTS: L[n] { L[2] { RPTID, V-list } }
    /// </summary>
    public class S6F11_data
    {
        /// <summary>
        /// 数据 ID，用于事件上报链路追踪与防重。
        /// </summary>
        public byte DATAID { get; set; }

        /// <summary>
        /// 事件 ID（CEID），通常使用无符号整型表示。
        /// </summary>
        public uint CEID { get; set; }

        /// <summary>
        /// 报告列表（每个报告包含 RPTID 与对应变量值集合）。
        /// </summary>
        public List<S6F11_report_data> Reports { get; set; } = new();

        /// <summary>
        /// 是否包含报告数据。
        /// </summary>
        public bool HasReports => Reports.Count > 0;

        /// <summary>
        /// 时间戳，用于内部记录发送时间和日志，单位为 UTC。
        /// </summary>
        public DateTime timeStamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// S6F11 中的单个报告项：L[2] { RPTID, V-list }
    /// </summary>
    public class S6F11_report_data
    {
        /// <summary>
        /// 报告 ID（RPTID）。
        /// </summary>
        public uint RPTID { get; set; }

        /// <summary>
        /// 报告值列表（对应 V-list）。
        /// 由于变量类型可变，统一用 object 承载，由上层按 VID/业务规则解析。
        /// </summary>
        public List<object?> Values { get; set; } = new();
    }
}
