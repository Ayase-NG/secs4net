using Secs4Net;
using SECSdata;
using static Secs4Net.Item;

namespace SECSbuilder
{
    /// <summary>
    /// S1F4 "Selected Equipment Status Data" 消息构建器。
    /// 常见结构：L[n] { SV... }
    /// </summary>
    public static class S1F4_builder
    {
        /// <summary>
        /// 根据状态列表构建 S1F4 消息。
        /// </summary>
        /// <param name="statusList">状态变量列表，编码时按列表顺序输出 SV。</param>
        /// <returns>可发送的 SecsMessage。</returns>
        public static SecsMessage Build(IEnumerable<S1F4_status_data>? statusList = null)
        {
            // 先准备 S1F4 的 SV 项列表。
            var svItems = new List<Item>();

            // if 关键分支：仅当传入列表不为空时才逐项编码。
            if (statusList != null)
            {
                // for each 关键分支：按顺序将每个状态值编码为 ASCII。
                foreach (var status in statusList)
                {
                    // if 关键分支：忽略空对象，避免空引用异常。
                    if (status == null)
                    {
                        continue;
                    }

                    svItems.Add(A(status.SV ?? string.Empty));
                }
            }

            // 构造并返回标准 S1F4 响应消息。
            return new SecsMessage(1, 4, replyExpected: false)
            {
                Name = "SelectedEquipmentStatusData",
                SecsItem = L(svItems.ToArray())
            };
        }

        /// <summary>
        /// 通过数据模型构建 S1F4 消息。
        /// </summary>
        /// <param name="data">S1F4 数据模型。</param>
        /// <returns>可发送的 SecsMessage。</returns>
        public static SecsMessage Build(S1F4_data data)
        {
            // if 关键分支：参数为空时直接抛出标准异常。
            ArgumentNullException.ThrowIfNull(data);

            // 复用主构建方法，保持编码逻辑单一来源。
            return Build(data.StatusList);
        }
    }
}
