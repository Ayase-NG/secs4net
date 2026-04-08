using Secs4Net;
using SECSdata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SECSparser
{
    /// <summary>
    /// S2F33 消息解析器。
    /// </summary>
    public static class S2F33_parser
    {
        /// <summary>
        /// 解析 S2F33 消息，返回强类型数据对象。
        /// </summary>
        public static S2F33_data Parse(SecsMessage msg)
        {
            // 1. 验证消息类型
            if (msg.S != 2 || msg.F != 33)
                throw new ArgumentException($"Invalid message type. Expected S2F33, but got S{msg.S}F{msg.F}.");

            // 2. 获取根列表
            var root = msg.SecsItem;
            if (root == null || root.Format != SecsFormat.List || root.Count < 2)
                throw new InvalidOperationException("Invalid S2F33 message format: missing root list.");

            var data = new S2F33_data();

            // 3. 解析 DATAID (第一个元素)
            var dataIdItem = root[0];
            if (dataIdItem != null && (dataIdItem.Format == SecsFormat.U1 || dataIdItem.Format == SecsFormat.Binary))
                data.DATAID = dataIdItem.FirstValueOrDefault<byte>(0);
            else
                throw new InvalidOperationException("Invalid S2F33 message format: DATAID not found.");

            // 4. 解析报告列表 (第二个元素)
            var reportsList = root[1];
            if (reportsList == null || reportsList.Format != SecsFormat.List)
                throw new InvalidOperationException("Invalid S2F33 message format: reports list not found.");

            // 处理删除所有报告的情况 (n = 0)
            if (reportsList.Count == 0)
                return data; // IsDeleteAll 为 true

            // 5. 遍历每个报告
            foreach (var reportItem in reportsList.Items)
            {
                if (reportItem.Format != SecsFormat.List || reportItem.Count < 2)
                    continue;

                // 解析 RPTID
                var rptIdItem = reportItem[0];
                uint rptId = rptIdItem?.FirstValueOrDefault<uint>(0) ?? 0;
                if (rptId == 0) continue;

                // 解析 VID 列表
                var vidList = new List<uint>();
                var vidsItem = reportItem[1];
                if (vidsItem.Format == SecsFormat.List)
                {
                    foreach (var vidItem in vidsItem.Items)
                    {
                        uint vid = vidItem?.FirstValueOrDefault<uint>(0) ?? 0;
                        if (vid != 0)
                            vidList.Add(vid);
                    }
                }

                // 如果 VID 列表为空，表示删除此 RPTID
                if (vidList.Count == 0)
                    data.Reports[rptId] = new List<uint>(); // 空列表表示删除
                else
                    data.Reports[rptId] = vidList;
            }

            return data;
        }
    }
}
