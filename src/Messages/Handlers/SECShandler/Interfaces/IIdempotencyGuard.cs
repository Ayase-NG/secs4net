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
    }
}
