using SECSdata;

namespace SECShandler.Interfaces
{
    /// <summary>
    /// 当前激活报警状态存储接口（供 S5F5/S5F6 查询使用）。
    /// </summary>
    public interface IAlarmStateStorage
    {
        /// <summary>
        /// 新增或更新一条激活报警。
        /// </summary>
        void UpsertAlarm(uint alid, byte alcd, string altx);

        /// <summary>
        /// 清除一条激活报警。
        /// </summary>
        void ClearAlarm(uint alid);

        /// <summary>
        /// 查询激活报警列表。
        /// </summary>
        IReadOnlyList<S5F6_alarm_data> GetActiveAlarms(IReadOnlyCollection<uint>? alidFilter);
    }
}
