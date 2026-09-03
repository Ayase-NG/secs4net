using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SECSdata
{
    /// <summary>
    /// S2F49 "Remote Command" 消息数据模型。
    /// </summary>
    public class S2F49_data
    {
        /// <summary>
        /// 消息数据的唯一标识符 (DATAID)，类型为无符号整数 (uint)。
        /// </summary>
        public uint DATAID { get; set; }

        /// <summary>
        /// 对象规范 (OBJSPEC)，类型为 ASCII 字符串。E39相关
        /// </summary>
        public string? OBJSPEC { get; set; }

        /// <summary>
        /// 远程命令名称 (RCMD)，类型为 ASCII 字符串。
        /// </summary>
        public string? RCMD { get; set; }

        /// <summary>
        /// 可变参数列表，每个参数包含名称(CPNAME)和值(CPVAL)。
        /// CPVAL 可能是多种数据类型 (string, int, double...)
        /// 为简化示例，此处统一存储为 Secs4Net.Item。
        /// </summary>
        public Dictionary<string, Secs4Net.Item> Parameters { get; set; } = new();

        /// <summary>
        /// 是否有参数。
        /// </summary>
        public bool HasParameters => Parameters.Count > 0;
    }
}
