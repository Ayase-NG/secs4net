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
        /// *停止触发事件列表（PRPAUSEEVENT）。
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
        /// *自动触发对应的 PortId。
        /// </summary>
        public string AutoStartedPortId { get; set; } = string.Empty;

        /// <summary>
        /// *自动触发对应的 LotId。
        /// </summary>
        public string AutoStartedLotId { get; set; } = string.Empty;

        /// <summary>
        /// 最近更新时间（UTC）。
        /// </summary>
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// 设备内部的 Carrier 执行状态，不等同于标准 PJ 状态；由设备确认事件驱动更新。
    /// </summary>
    public enum ProcessJobCarrierState
    {
        /// <summary>
        /// 计划已创建，等待物料或执行条件；零值兼容未包含状态字段的旧快照。
        /// </summary>
        Waiting = 0,

        /// <summary>
        /// Carrier 已到位、Mapping 校验通过，满足当前调度条件。
        /// </summary>
        Ready = 1,

        /// <summary>
        /// 设备已确认开始加工，不能仅根据 START 发送成功设置。
        /// </summary>
        Processing = 2,

        /// <summary>
        /// 选定槽位加工完成且晶圆归位等收尾已确认，不代表 PJ 已归档或占用已释放。
        /// </summary>
        Completed = 3,

        /// <summary>
        /// 尚未加工的任务已确认取消。
        /// </summary>
        Cancelled = 4,

        /// <summary>
        /// 任务已确认终止并完成必要的安全收尾，不用于表示可恢复的临时故障。
        /// </summary>
        Aborted = 5
    }

    /// <summary>
    /// ProcessJob 下的单个 Carrier 定义及执行状态；完成后保留条目供重传比较和归档。
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

        /// <summary>
        /// 当前执行状态；新计划默认等待，运行状态不参与创建请求的定义比较。
        /// </summary>
        public ProcessJobCarrierState State { get; set; } = ProcessJobCarrierState.Waiting;
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
