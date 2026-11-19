namespace SECShandler.Interfaces
{
    /// <summary>
    /// 可供 PJ 创建、Mapping 完成等触发点复用的设备级执行入口，不创建后台线程。
    /// </summary>
    public interface IProcessJobExecutionCoordinator
    {
        /// <summary>
        /// 尝试启动一个满足物料条件的自动 PJ Carrier；未就绪、忙碌或执行失败时返回 false。
        /// </summary>
        Task<bool> TryExecuteAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// 提供 LP 的当前物料快照；MapSlotList 按槽位 1～25 排列，0=空、1=有片、2=故障。
    /// </summary>
    public interface IProcessJobMaterialSource
    {
        /// <summary>
        /// 返回当前可读取的端口及 Mapping；缺失或格式无效的数据不得被视为就绪。
        /// </summary>
        IReadOnlyList<ProcessJobPortSnapshot> GetPorts();
    }

    /// <summary>
    /// 在事件成功报告 Host 后确认本次更新的物料字段，防止复用前一 Carrier 的 Mapping。
    /// </summary>
    public interface IProcessJobMaterialObserver
    {
        /// <summary>
        /// 在物料字段更新前使旧 Mapping 失效，失败或未报告的事件也不能保留旧就绪证据。
        /// </summary>
        void InvalidateReportedValues(IReadOnlyCollection<string> parameterNames);

        /// <summary>
        /// 参数名区分字符串和类型化值来源；同一事件同时提供时以类型化值为准。
        /// </summary>
        void ConfirmReportedValues(IReadOnlyCollection<string> parameterNames, IReadOnlyCollection<string> typedParameterNames);
    }

    /// <summary>
    /// 执行已确认的单目标配方选择及启动；任一步拒绝或结果不确定必须抛出异常。
    /// </summary>
    public interface IProcessJobExecutionClient
    {
        /// <summary>
        /// 在同一个目标实例顺序调用 RecipeSelect、StartMeasurement，均明确成功后返回，禁止自动重试或广播。
        /// </summary>
        Task SelectRecipeAndStartAsync(ProcessJobExecutionRequest request, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 一次物料读取结果；调用方应重新读取，不能缓存为永久就绪状态。
    /// </summary>
    public sealed record ProcessJobPortSnapshot(string PortId, string CarrierId, IReadOnlyList<byte> MapSlotList, string LotId);

    /// <summary>
    /// 固定本次执行的 PJ、Carrier、端口、配方及计划槽位，不能用 Mapping 覆盖 Host 选择的槽位。
    /// </summary>
    public sealed record ProcessJobExecutionRequest(string PJID, string CarrierId, string PortId,
        string RecipeId, string Mode, string LotId, IReadOnlyList<uint> Slots);
}
