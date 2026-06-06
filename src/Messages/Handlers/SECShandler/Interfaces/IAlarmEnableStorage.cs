namespace SECShandler.Interfaces
{
    /// <summary>
    /// 报警上报使能存储接口（S5F3/S5F4）。
    /// </summary>
    public interface IAlarmEnableStorage
    {
        /// <summary>
        /// 设置单个报警 ID 的上报使能状态。
        /// </summary>
        void SetAlarmEnabled(uint alid, bool enabled);

        /// <summary>
        /// 批量设置全部报警的上报使能状态。
        /// </summary>
        void SetAllAlarmsEnabled(bool enabled);

        /// <summary>
        /// 判断指定报警 ID 是否允许上报。
        /// </summary>
        bool IsAlarmEnabled(uint alid);

        /// <summary>
        /// 获取当前显式启用的报警 ID 列表。
        /// </summary>
        IReadOnlyList<uint> GetEnabledAlarmIds();
    }
}
