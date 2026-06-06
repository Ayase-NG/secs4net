using System;
using System.Collections.Generic;

namespace SECSdata
{
    /// <summary>
    /// S16F15 "Create Process Job" 请求数据模型。
    /// 常见结构：L[2] { DATAID, PROCESS_JOB_LIST }。
    /// 还未补充Recipe和暂停CEID相关执行
    /// </summary>
    public class S16F15_data
    {
        /// <summary>
        /// 数据 ID（DATAID）。
        /// </summary>
        public uint DATAID { get; set; }

        /// <summary>
        /// Process Job 列表（最小骨架仅抽取关键字段）。
        /// </summary>
        public List<S16F15_process_job_data> ProcessJobs { get; set; } = new();

        /// <summary>
        /// 请求中的 Process Job 数量。
        /// </summary>
        public int ProcessJobCount => ProcessJobs.Count;

        /// <summary>
        /// 时间戳，用于内部记录接收时间和日志追踪，单位为 UTC 时间。
        /// </summary>
        public DateTime timeStamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// S16F15 中的单个 Process Job 最小字段模型。
    /// </summary>
    public class S16F15_process_job_data
    {
        /// <summary>
        /// Process Job ID。
        /// </summary>
        public string PJID { get; set; } = string.Empty;

        /// <summary>
        /// 格式判断(Material Format),常见为 MF=0x0D(Carrier + Slot)
        /// </summary>
        public string MF { get; set; } = string.Empty;

        /// <summary>
        /// 配方 ID（来自 PROCESS_SPEC 的第 2 项）。
        /// </summary>
        public string RecipeId { get; set; } = string.Empty;
        
        /// <summary>
        /// 是否自动作业。
        /// </summary>
        public bool PRPROCESSSTART { get; set; } = true;

        /// <summary>
        /// 存入CEID列表，定义停止触发事件。
        /// </summary>
        public List<uint> PRPAUSEEVENT { get; set; } = new();
        /// <summary>
        /// 该作业包含的载具列表。
        /// </summary>
        public List<S16F15_carrier_data> Carriers { get; set; } = new();
    }

    /// <summary>
    /// S16F15 中单个载具最小字段模型。
    /// </summary>
    public class S16F15_carrier_data
    {
        /// <summary>
        /// 载具 ID。
        /// </summary>
        public string CarrierId { get; set; } = string.Empty;

        /// <summary>
        /// 槽位映射列表。
        /// </summary>
        public List<uint> Slots { get; set; } = new();
    }
}
