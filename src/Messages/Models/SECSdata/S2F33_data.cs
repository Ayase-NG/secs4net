using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SECSdata
{
    /// <summary>
    /// 表示 S2F33 "Define Report" 消息的数据模型。
    /// 主机使用此消息来删除及定义数据报告的构成（一个RPTID包含哪些VID）。
    /// </summary>
    public class S2F33_data
    {
        /// <summary>
        /// 数据ID，用于标识消息，通常递增。
        /// </summary>
        public byte DATAID { get; set; }

        /// <summary>
        /// 要定义的报告列表。
        /// Key: RPTID (报告ID), Value: 该报告包含的VID列表。
        /// </summary>
        public Dictionary<uint, List<uint>> Reports { get; set; } = new();

        /// <summary>
        /// 要删除的特定报告ID列表（对应变量列表为空的项）。
        /// </summary>
        public List<uint> DeletedRptIds { get; set; } = new();

        /// <summary>
        /// 要定义或更新的报告字典。
        /// Key: RPTID, Value: 该报告包含的 VID 列表。
        /// </summary>
        public Dictionary<uint, List<uint>> DefinedReports { get; set; } = new();

        /// <summary>
        /// 是否为删除所有报告的指令。
        /// </summary>
        public bool IsDeleteAll => Reports.Count == 0;

        /// <summary>
        /// 获取所有报告中定义的RPTID列表。
        /// </summary>
        public List<uint> AllRptIds => Reports.Keys.ToList();

        /// <summary>
        /// 时间戳，用于内部记录发送时间和打印日志，单位为UTC时间
        /// </summary>
        public DateTime timeStamp { get; set; } = DateTime.UtcNow;
    }
}
