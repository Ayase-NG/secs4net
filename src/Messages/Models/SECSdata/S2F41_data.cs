using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Secs4Net;

namespace SECSdata
{
    /// <summary>
    /// S2F41 "Remote Command" 消息数据模型。
    /// SML样例：&lt;L [2] &lt;A "START"&gt; &lt;L [2] &lt;L [2] &lt;A "LOTID"&gt; &lt;A "26P123456"&gt; &gt; &lt;L [2] &lt;A "PPID"&gt; &lt;A "RCP001"&gt; &gt; &gt; &gt;。
    /// </summary>
    public class S2F41_data
    {
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
