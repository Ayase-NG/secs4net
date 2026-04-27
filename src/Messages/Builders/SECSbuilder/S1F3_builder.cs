using Secs4Net;
using SECSdata;
using static Secs4Net.Item;

namespace SECSbuilder
{
    /// <summary>
    /// S1F3 "Selected Equipment Status Request" 消息构建器。
    /// 常见结构：L[n] { SVID... }
    /// </summary>
    public static class S1F3_builder
    {
        /// <summary>
        /// 根据 SVID 列表构建 S1F3 消息。
        /// </summary>
        /// <param name="svidList">状态变量 ID 列表；为空时表示请求全部状态变量。</param>
        public static SecsMessage Build(IEnumerable<uint>? svidList = null)
        {
            var svidItems = new List<Item>();
            if (svidList != null)
            {
                foreach (var svid in svidList)
                {
                    svidItems.Add(U4(svid));
                }
            }

            return new SecsMessage(1, 3, replyExpected: true)
            {
                Name = "SelectedEquipmentStatusRequest",
                SecsItem = L(svidItems.ToArray())
            };
        }

        /// <summary>
        /// 通过数据模型构建 S1F3 消息。
        /// </summary>
        public static SecsMessage Build(S1F3_data data)
        {
            ArgumentNullException.ThrowIfNull(data);
            return Build(data.SVIDList);
        }
    }
}
