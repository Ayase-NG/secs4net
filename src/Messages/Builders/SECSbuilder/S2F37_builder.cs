using Secs4Net;
using SECSdata;
using System;
using System.Collections.Generic;
using static Secs4Net.Item;

namespace SECSbuilder
{
    /// <summary>
    /// S2F37 "Enable/Disable Event Report" 消息构建器。
    /// 常见结构：L[2] { CEED, CEIDLIST }
    /// </summary>
    public static class S2F37_builder
    {
        /// <summary>
        /// 根据 CEED 和 CEID 列表构建 S2F37 消息。
        /// </summary>
        /// <param name="ceed">启用/禁用代码，常见：0=Disable, 1=Enable。</param>
        /// <param name="ceidList">CEID 列表；为空表示全部事件。</param>
        public static SecsMessage Build(byte ceed, IEnumerable<uint>? ceidList = null)
        {
            var ceidItems = new List<Item>();
            if (ceidList != null)
            {
                foreach (var ceid in ceidList)
                {
                    ceidItems.Add(U4(ceid));
                }
            }

            return new SecsMessage(2, 37, replyExpected: true)
            {
                Name = "EnableDisableEventReport",
                SecsItem = L(
                    B(ceed),
                    L(ceidItems.ToArray())
                )
            };
        }

        /// <summary>
        /// 通过数据模型构建 S2F37 消息。
        /// </summary>
        public static SecsMessage Build(S2F37_data data)
        {
            ArgumentNullException.ThrowIfNull(data);
            return Build(data.CEED, data.CEIDList);
        }
    }
}
