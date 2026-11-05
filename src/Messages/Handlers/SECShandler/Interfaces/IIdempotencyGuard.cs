namespace SECShandler.Interfaces
{
    /// <summary>
    /// 幂等保护接口。
    /// 用于在短时间窗口内拦截重复业务请求，避免重复执行。
    /// </summary>
    public interface IIdempotencyGuard
    {
        /// <summary>
        /// 尝试开始一次幂等执行。
        /// true=首次执行，false=命中重复。
        /// </summary>
        bool TryBegin(string scope, string key, TimeSpan ttl);

        /// <summary>
        /// 尝试占用短期去重记录；成功时返回本次占用令牌，重复或未启用保护时令牌为空。
        /// </summary>
        bool TryBegin(string scope, string key, TimeSpan ttl, out Guid ownershipToken);

        /// <summary>
        /// 仅在记录仍属于指定占用令牌时原子删除；不删除其他请求的新记录。
        /// </summary>
        bool TryRemove(string scope, string key, Guid ownershipToken);
    }
}
