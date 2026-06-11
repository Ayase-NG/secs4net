namespace SECShandler.Interfaces
{
    /// <summary>
    /// S14F9/S16F15 作业计划运行态缓存接口。
    /// 用于缓存 Host 下发的 ControlJob/ProcessJob，并在 Carrier 到达时做关联调度。
    /// </summary>
    public interface IJobPlanStorage
    {
        /// <summary>
        /// 新增或更新 ProcessJob 计划。
        /// </summary>
        void UpsertProcessJob(ProcessJobPlan plan);

        /// <summary>
        /// 按 PJID 查询 ProcessJob 计划。
        /// </summary>
        bool TryGetProcessJob(string pjId, out ProcessJobPlan plan);

        /// <summary>
        /// 获取当前全部 ProcessJob 快照。
        /// </summary>
        IReadOnlyList<ProcessJobPlan> GetAllProcessJobs();

        /// <summary>
        /// 新增或更新 ControlJob 计划。
        /// </summary>
        void UpsertControlJob(ControlJobPlan plan);

        /// <summary>
        /// 按 CJID 查询 ControlJob 计划。
        /// </summary>
        bool TryGetControlJob(string cjId, out ControlJobPlan plan);

        /// <summary>
        /// 获取当前全部 ControlJob 快照。
        /// </summary>
        IReadOnlyList<ControlJobPlan> GetAllControlJobs();

        /// <summary>
        /// 标记指定 PJID 已自动触发执行，避免重复自动启动。
        /// </summary>
        void MarkProcessJobAutoStarted(string pjId, string portId, string lotId);
    }

    /// <summary>
    /// ProcessJob 计划运行态模型。
    /// </summary>
    public sealed class ProcessJobPlan
    {
        /// <summary>
        /// Process Job ID。
        /// </summary>
        public string PJID { get; set; } = string.Empty;

        /// <summary>
        /// 配方 ID。
        /// </summary>
        public string RecipeId { get; set; } = string.Empty;

        /// <summary>
        /// 是否自动开始（PRPROCESSSTART）。
        /// </summary>
        public bool AutoStart { get; set; }

        /// <summary>
        /// 停止触发事件列表（PRPAUSEEVENT）。
        /// </summary>
        public List<uint> PauseEvents { get; set; } = new();

        /// <summary>
        /// 计划中的载具条目。
        /// </summary>
        public List<ProcessJobCarrierPlan> Carriers { get; set; } = new();

        /// <summary>
        /// 是否已自动触发过执行。
        /// </summary>
        public bool AutoStarted { get; set; }

        /// <summary>
        /// 自动触发对应的 PortId。
        /// </summary>
        public string AutoStartedPortId { get; set; } = string.Empty;

        /// <summary>
        /// 自动触发对应的 LotId。
        /// </summary>
        public string AutoStartedLotId { get; set; } = string.Empty;

        /// <summary>
        /// 最近更新时间（UTC）。
        /// </summary>
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// ProcessJob 下的单个 Carrier 计划。
    /// </summary>
    public sealed class ProcessJobCarrierPlan
    {
        /// <summary>
        /// CarrierId。
        /// </summary>
        public string CarrierId { get; set; } = string.Empty;

        /// <summary>
        /// 计划槽位列表。
        /// </summary>
        public List<uint> Slots { get; set; } = new();
    }

    /// <summary>
    /// ControlJob 计划运行态模型。
    /// </summary>
    public sealed class ControlJobPlan
    {
        /// <summary>
        /// Control Job ID。
        /// </summary>
        public string CJID { get; set; } = string.Empty;

        /// <summary>
        /// 关联的 PJID 列表（来自 ProcessingCtrlSpec）。
        /// </summary>
        public List<string> ProcessingCtrlSpec { get; set; } = new();

        /// <summary>
        /// Carrier 输入列表（来自 CarrierInputSpec）。
        /// </summary>
        public List<string> CarrierInputSpec { get; set; } = new();

        /// <summary>
        /// 启动方式（StartMethod）。
        /// </summary>
        public bool? StartMethod { get; set; }

        /// <summary>
        /// 最近更新时间（UTC）。
        /// </summary>
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
