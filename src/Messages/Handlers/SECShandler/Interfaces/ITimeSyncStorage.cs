namespace SECShandler.Interfaces
{
    /// <summary>
    /// 时间同步状态存储接口（S2F31/S2F32）。
    /// </summary>
    public interface ITimeSyncStorage
    {
        /// <summary>
        /// 更新 Host 下发的时间同步值。
        /// </summary>
        void UpdateHostTime(DateTime hostTimeUtc, string rawTimeText);

        /// <summary>
        /// 获取最近一次同步的 Host 时间（UTC）。
        /// </summary>
        DateTime? GetLastHostTimeUtc();

        /// <summary>
        /// 获取最近一次同步的原始时间文本。
        /// </summary>
        string GetLastHostTimeRaw();
    }
}
