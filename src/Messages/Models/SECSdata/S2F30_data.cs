using System;
using System.Collections.Generic;

namespace SECSdata
{
    /// <summary>
    /// S2F30 "Equipment Constant Namelist" 消息数据模型。
    /// 常见结构：L[n] { L[6] { ECID, ECNAME, ECMIN, ECMAX, ECDEF, UNITS } ... }。
    /// </summary>
    public class S2F30_data
    {
        /// <summary>
        /// 设备常量定义列表。
        /// </summary>
        public List<S2F30_constant_data> ConstantList { get; set; } = new();

        /// <summary>
        /// 时间戳，用于内部记录发送时间，单位 UTC。
        /// </summary>
        public DateTime timeStamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// S2F30 中的单个设备常量定义项。
    /// </summary>
    public class S2F30_constant_data
    {
        /// <summary>
        /// 设备常量 ID（ECID）。
        /// </summary>
        public uint ECID { get; set; }

        /// <summary>
        /// 设备常量名称（ECNAME）。
        /// </summary>
        public string ECNAME { get; set; } = string.Empty;

        /// <summary>
        /// 设备常量最小值（ECMIN）。
        /// </summary>
        public string ECMIN { get; set; } = string.Empty;

        /// <summary>
        /// 设备常量最大值（ECMAX）。
        /// </summary>
        public string ECMAX { get; set; } = string.Empty;

        /// <summary>
        /// 设备常量默认值（ECDEF）。
        /// </summary>
        public string ECDEF { get; set; } = string.Empty;

        /// <summary>
        /// 工程单位（UNITS）。
        /// </summary>
        public string UNITS { get; set; } = string.Empty;

        /// <summary>
        /// ECMIN、ECMAX、ECDEF 使用的 SECS-II 数据格式。
        /// </summary>
        public string Format { get; set; } = "A";
    }
}
