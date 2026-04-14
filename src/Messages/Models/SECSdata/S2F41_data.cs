using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SECSdata
{
    public class S2F41_data
    {
        /// <summary>
        /// 远程命令名称 (RCMD)，类型为 ASCII 字符串。
        /// </summary>
        public string? RCMD { get; set; }

        /// <summary>
        /// 可变参数列表，每个参数包含名称(CPNAME)和值(CPVAL)。
        /// CPVAL 可能是多种数据类型 (string, int, double...)
        /// 为简化示例，此处统一存储为 object。
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; } = new();

        /// <summary>
        /// 是否有参数。
        /// </summary>
        public bool HasParameters => Parameters.Count > 0;
    }
}
