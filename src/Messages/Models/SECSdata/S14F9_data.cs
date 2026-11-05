using System;
using System.Collections.Generic;

namespace SECSdata
{
    /// <summary>
    /// S14F9 Create Object 请求的 ControlJob 最小数据模型。
    /// 结构为 L[3] { OBJSPEC, OBJTYPE, L[n] { L[2] { ATTRID, ATTRDATA } } }。
    /// </summary>
    public class S14F9_data
    {
        /// <summary>
        /// 对象规范（OBJSPEC），标识创建对象的作用范围。
        /// </summary>
        public string OBJSPEC { get; set; } = string.Empty;

        /// <summary>
        /// 对象类型（常见值如 ControlJob）。
        /// </summary>
        public string OBJTYPE { get; set; } = string.Empty;

        /// <summary>
        /// 为兼容现有处理层保留的集合；单条 Create Object 请求解析为一个对象。
        /// </summary>
        public List<S14F9_control_job_data> ControlJobs { get; set; } = new();

        /// <summary>
        /// 请求中的对象明细数量。
        /// </summary>
        public int ObjectCount => ControlJobs.Count;

        /// <summary>
        /// 时间戳，用于内部记录接收时间和日志追踪，单位为 UTC 时间。
        /// </summary>
        public DateTime timeStamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// S14F9 中的单个 Control Job 最小字段模型。
    /// </summary>
    public class S14F9_control_job_data
    {
        /// <summary>
        /// Control Job 对象标识（ObjID）。
        /// </summary>
        public string ObjID { get; set; } = string.Empty;

        /// <summary>
        /// 处理工艺作业，处理PRJOB_ID。
        /// </summary>
        public List<string> ProcessingCtrlSpec { get; set; } = new();

        /// <summary>
        /// 载具输入列表（CarrierInputSpec）。
        /// </summary>
        public List<string> CarrierInputSpec { get; set; } = new();

        /// <summary>
        /// 启动方式（StartMethod），若未携带则保持 null。
        /// </summary>
        public bool? StartMethod { get; set; }
    }
}
