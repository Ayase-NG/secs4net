using System;
using System.Collections.Generic;

namespace SECSdata
{
    public class S2F29_data
    {
        /// <summary>
        /// 设备常量 ID 列表（ECID）。
        /// </summary>
        public List<uint> ECIDList { get; set; } = new();

        /// <summary>
        /// 是否请求全部设备常量（ECID 列表为空时）。
        /// </summary>
        public bool IsAllEcid => ECIDList.Count == 0;

        /// <summary>
        /// 时间戳，用于内部记录接收时间，单位 UTC。
        /// </summary>
        public DateTime timeStamp { get; set; } = DateTime.UtcNow;
    }
}
