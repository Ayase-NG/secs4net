namespace SECShandler.Interfaces
{
    /// <summary>
    /// 日期时间查询及同步状态接口（S2F17/S2F18、S2F31/S2F32）。
    /// </summary>
    public interface ITimeSyncStorage
    {
        /// <summary>
        /// 设置设备逻辑时钟并保存 Host 原始时间；设置失败必须抛出异常，不能报告完成。
        /// </summary>
        void UpdateHostTime(DateTime hostTimeUtc, string rawTimeText);

        /// <summary>
        /// 获取持续走时的设备 UTC 时间；未同步时使用系统 UTC，不修改操作系统时钟。
        /// </summary>
        DateTime GetEquipmentTimeUtc();

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
