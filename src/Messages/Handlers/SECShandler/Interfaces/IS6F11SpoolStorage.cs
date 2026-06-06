using SECSdata;

namespace SECShandler.Interfaces
{
    /// <summary>
    /// S6F11 断线缓存存储接口。
    /// </summary>
    public interface IS6F11SpoolStorage
    {
        /// <summary>
        /// 入队一条 S6F11 事件。
        /// </summary>
        void Enqueue(S6F11_data data);

        /// <summary>
        /// 按批次读取待补发数据（不移除）。
        /// </summary>
        IReadOnlyList<S6F11_data> PeekBatch(int maxCount);

        /// <summary>
        /// 确认并移除前 count 条已成功补发的数据。
        /// </summary>
        void AckBatch(int count);

        /// <summary>
        /// 获取当前缓存条数。
        /// </summary>
        int Count { get; }
    }
}
