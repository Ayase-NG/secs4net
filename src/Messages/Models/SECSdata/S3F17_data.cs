using System;
using System.Collections.Generic;
using Secs4Net;

namespace SECSdata
{
    /// <summary>
    /// S3F17 "Carrier Action Request" 数据模型。
    /// 结构：L[5] { DATAID, CARRIERACTION, CARRIERID, PTN, L[n] { L[2] { CATTRID, CATTRDATA } } }。
    /// </summary>
    public class S3F17_data
    {
        /// <summary>
        /// 消息数据标识符（DATAID）。
        /// </summary>
        public uint DATAID { get; set; }

        /// <summary>
        /// 载具操作名称（CARRIERACTION）。
        /// </summary>
        public string CARRIERACTION { get; set; } = string.Empty;

        /// <summary>
        /// 载具编号（CARRIERID）。
        /// </summary>
        public string CARRIERID { get; set; } = string.Empty;

        /// <summary>
        /// 端口号（PTN）。
        /// </summary>
        public byte PTN { get; set; }

        /// <summary>
        /// 载具属性列表，每项包含 CATTRID 和保留原始格式的 CATTRDATA。
        /// </summary>
        public List<S3F17_carrier_attribute_data> CarrierAttributes { get; set; } = new();

        /// <summary>
        /// 时间戳，用于内部记录接收时间和日志追踪，单位为 UTC 时间。
        /// </summary>
        public DateTime timeStamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// S3F17 中的单项载具属性。
    /// </summary>
    public class S3F17_carrier_attribute_data
    {
        /// <summary>
        /// 载具属性标识符（CATTRID）。
        /// </summary>
        public string CATTRID { get; set; } = string.Empty;

        /// <summary>
        /// 载具属性数据（CATTRDATA），保留其 SECS-II 原始类型。
        /// </summary>
        public Item CATTRDATA { get; set; } = null!;
    }
}
