namespace SECShandler.Interfaces
{
    /// <summary>
    /// Port 维度运行态上下文存储接口。
    /// </summary>
    public interface IPortContextStorage
    {
        /// <summary>
        /// 按 PortId 新增或更新上下文。
        /// </summary>
        void Upsert(PortRuntimeContext context);

        /// <summary>
        /// 按 PortId 查询上下文。
        /// </summary>
        bool TryGetByPortId(string portId, out PortRuntimeContext context);

        /// <summary>
        /// 按 LotId 查询上下文（用于结果上报缺失 PortId 的兜底）。
        /// </summary>
        bool TryGetByLotId(string lotId, out PortRuntimeContext context);

        /// <summary>
        /// 按 PortId 清理上下文。
        /// </summary>
        void RemoveByPortId(string portId);

        /// <summary>
        /// 获取当前全部 Port 上下文快照。
        /// </summary>
        IReadOnlyList<PortRuntimeContext> GetAll();
    }

    /// <summary>
    /// Port 维度运行态上下文。
    /// </summary>
    public sealed class PortRuntimeContext
    {
        public string PortId { get; set; } = string.Empty;
        public string CarrierId { get; set; } = string.Empty;
        public string LotId { get; set; } = string.Empty;
        public string SlotsList { get; set; } = string.Empty;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
