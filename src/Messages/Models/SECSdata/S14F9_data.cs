using System;
using System.Collections.Generic;

namespace SECSdata
{
    /// <summary>
    /// S14F9 "Create Control Job" 请求数据模型。
    /// 常见结构：L[n]，前两项通常为对象域与对象类型，第 3 项为控制作业对象列表。
    /// </summary>
    public class S14F9_data
    {
        /// <summary>
        /// 对象域（常见值如 Equipment）。
        /// </summary>
        public string ObjectDomain { get; set; } = string.Empty;

        /// <summary>
        /// 对象类型（常见值如 ControlJob）。
        /// </summary>
        public string ObjectType { get; set; } = string.Empty;

        /// <summary>
        /// Control Job 列表（最小骨架仅抽取关键字段）。
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
        /// 载具输入列表（CarrierInputSpec）。
        /// </summary>
        public List<string> CarrierInputSpec { get; set; } = new();

        /// <summary>
        /// 启动方式（StartMethod），若未携带则保持 null。
        /// </summary>
        public bool? StartMethod { get; set; }
    }
}
