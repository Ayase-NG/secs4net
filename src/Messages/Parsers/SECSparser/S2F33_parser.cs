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

            // 3. 解析 DATAID (第一个元素)，使用通用工具进行类型匹配（仅接受 U1/U2/U4），不匹配时抛出异常
            var dataIdItem = root[0];
            if (dataIdItem == null)
                throw new InvalidOperationException("Invalid S2F33 message format: DATAID not found.");

            // 使用 SecsItemHelper 提供的通用方法，类型错误会以 InvalidOperationException 抛出并传到上层
            uint dataIdUint = dataIdItem.GetUIntId("DATA");
            if (dataIdUint > byte.MaxValue)
                throw new InvalidOperationException("Invalid S2F33 message format: DATAID out of byte range.");
            data.DATAID = (byte)dataIdUint;

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
                // 每个 reportItem 应为 List, 且至少包含 RPTID（第二个元素可选）
                if (reportItem == null || reportItem.Format != SecsFormat.List || reportItem.Count < 1)
                    continue;

                // 解析 RPTID，使用通用工具方法，若无法解析则跳过该项
                var rptIdItem = reportItem[0];
                uint rptId;
                try
                {
                    // 支持被单元素 List 包装的情况
                    if (rptIdItem.Format == SecsFormat.List && rptIdItem.Count > 0)
                        rptId = rptIdItem[0].GetUIntId("RPT");
                    else
                        rptId = rptIdItem.GetUIntId("RPT");
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                if (rptId == 0) continue;

                // 解析 VID 列表（如果存在）
                var vidList = new List<uint>();
                if (reportItem.Count > 1)
                {
                    var vidsItem = reportItem[1];
                    if (vidsItem != null && vidsItem.Format == SecsFormat.List)
                    {
                        foreach (var vidItem in vidsItem.Items)
                        {
                            try
                            {
                                uint vid;
                                if (vidItem.Format == SecsFormat.List && vidItem.Count > 0)
                                    vid = vidItem[0].GetUIntId("VID");
                                else
                                    vid = vidItem.GetUIntId("VID");

                                if (vid != 0)
                                    vidList.Add(vid);
                            }
                            catch (InvalidOperationException)
                            {
                                // 忽略单个 VID 类型错误
                                continue;
                            }
                        }
                    }
                }

                if (vidList.Count == 0)
                {
                    // 表示删除该 RPTID
                    data.Reports[rptId] = new List<uint>();
                    data.DeletedRptIds.Add(rptId);
                }
                else
                {
                    data.Reports[rptId] = vidList;
                    data.DefinedReports[rptId] = vidList;
                }
            }

            return data;
        }

        // 使用通用 SecsItemHelper 中的方法来读取数值，避免重复实现
    }
}
