using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Secs4Net;

namespace SECSdata
{
    /// <summary>
    /// S6F11 "Event Report Send" 数据模型。
    /// 常见结构：L[3] { DATAID, CEID, REPORTS }
    /// REPORTS: L[n] { L[2] { RPTID, V-list } }
    /// 项目当前编码约定的 SML 样例（VID/CPVal）：
    /// &lt;L [3]
    ///   &lt;U1 1&gt;
    ///   &lt;U4 1001&gt;
    ///   &lt;L [1]
    ///     &lt;L [2]
    ///       &lt;U4 1000&gt;
    ///       &lt;L [2]
    ///         &lt;L [2] &lt;U2 1000&gt; &lt;A "26P123456"&gt; &gt;
    ///         &lt;L [2] &lt;U2 2000&gt; &lt;A "RCP001"&gt; &gt;
    ///       &gt;
    ///     &gt;
    ///   &gt;
    /// &gt;
    /// </summary>
    public class S6F11_data
    {
        /// <summary>
        /// 数据 ID，用于事件上报链路追踪与防重。
        /// </summary>
        public byte DATAID { get; set; }

        /// <summary>
        /// 事件 ID（CEID），通常使用无符号整型表示。
        /// </summary>
        public uint CEID { get; set; }

        /// <summary>
        /// 报告列表（每个报告包含 RPTID 与对应变量值集合）。
        /// </summary>
        public List<S6F11_report_data> Reports { get; set; } = new();

        /// <summary>
        /// 是否包含报告数据。
        /// </summary>
        public bool HasReports => Reports.Count > 0;

        /// <summary>
        /// 时间戳，用于内部记录发送时间和日志，单位为 UTC。
        /// </summary>
        public DateTime timeStamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// S6F11 中的单个报告项：L[2] { RPTID, V-list }
    /// </summary>
    public class S6F11_report_data
    {
        /// <summary>
        /// 报告 ID（RPTID）。
        /// </summary>
        public uint RPTID { get; set; }

        /// <summary>
        /// 报告值列表（V-list）。
        /// 当前项目每个元素结构为：L[2] { VID(U2), CPVal(任意 SECS-II Item) }。
        /// </summary>
        public List<S6F11_parameter_data> Values { get; set; } = new();
    }

    /// <summary>
    /// S6F11 参数项：VID/CPName/CPVal。
    /// 上报编码使用 VID + CPVal；CPName 仅用于内部追踪与调试。
    /// </summary>
    public class S6F11_parameter_data
    {
        /// <summary>
        /// 参数编号（VID）。
        /// </summary>
        public ushort VID { get; set; }

        /// <summary>
        /// 参数名称（CPName），用于内部可读性。
        /// </summary>
        public string CPName { get; set; } = string.Empty;

        /// <summary>
        /// 兼容现有 ASCII 参数值；未设置 ValueItem 时编码为 A。
        /// </summary>
        public string CPVal { get; set; } = string.Empty;

        /// <summary>
        /// 类型化的参数值（包含 List、数值等），优先于 CPVal 字符串参与编码。
        /// </summary>
        [JsonIgnore]
        public Item? ValueItem
        {
            get
            {
                // 无类型化值时由构建器回退到现有 ASCII 文本。
                if (EncodedValue is not { Length: > 0 })
                {
                    return null;
                }

                var bytes = new ReadOnlySequence<byte>(EncodedValue);
                var item = Item.DecodeFromFullBuffer(ref bytes);
                if (!bytes.IsEmpty)
                {
                    throw new InvalidOperationException("Invalid S6F11 encoded value: trailing data.");
                }

                return item;
            }
            set
            {
                // 清空类型化值时恢复使用 CPVal 文本。
                if (value is null)
                {
                    EncodedValue = null;
                    return;
                }

                var buffer = new ArrayBufferWriter<byte>();
                value.EncodeTo(buffer);
                EncodedValue = buffer.WrittenSpan.ToArray();
            }
        }

        /// <summary>
        /// ValueItem 的 SECS-II 二进制表示，供 S6F11 暂存快照跨重启保留原始类型和值。
        /// </summary>
        public byte[]? EncodedValue { get; set; }
    }
}
