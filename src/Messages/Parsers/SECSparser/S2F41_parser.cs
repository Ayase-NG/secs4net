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
    /// S2F41 (Remote Command) 消息解析器。
    /// 负责将主机发送的原始 SECS 消息转换为强类型的 S2F41_Data 对象。
    /// </summary>
    public static class S2F41_parser
    {
        /// <summary>
        /// 解析 S2F41 消息。
        /// </summary>
        /// <param name="msg">Secs4Net 接收到的原始消息，需保证非空。</param>
        /// <returns>解析后的 S2F41_Data 对象，包含命令名和参数列表。</returns>
        /// <exception cref="ArgumentException">消息类型不是 S2F41 时抛出。</exception>
        /// <exception cref="InvalidOperationException">消息格式不符合预期时抛出（如缺少必要字段、类型错误）。</exception>
        public static S2F41_data Parse(SecsMessage msg)
        {
            // 1. 消息类型校验：必须为 S2F41
            if (msg.S != 2 || msg.F != 41)
                throw new ArgumentException($"Invalid message type. Expected S2F41, but got S{msg.S}F{msg.F}.");

            // 2. 获取根列表（Root List），标准格式为：<L [2] ...>
            //    - 第一个元素：RCMD (ASCII 字符串)
            //    - 第二个元素：参数列表 (List of parameters, 可能为空)
            var root = msg.SecsItem;
            if (root == null || root.Format != SecsFormat.List || root.Count < 2)
                throw new InvalidOperationException("Invalid S2F41 message format: root list missing or invalid (expected at least 2 items).");

            var data = new S2F41_data();

            // 3. 解析 RCMD（远程命令名称）
            //    类型必须为 ASCII 字符串，否则认为格式错误
            var rcmdItem = root[0];
            if (rcmdItem == null || rcmdItem.Format != SecsFormat.ASCII)
                throw new InvalidOperationException("Invalid S2F41 message: RCMD (first element) not found or not an ASCII string.");
            data.RCMD = rcmdItem.GetString();  // 获取字符串值，例如 "START", "STOP" 等

            // 4. 解析参数列表（第二个元素）
            //    参数列表是一个 List，每个参数又是一个 List [CPNAME, CPVAL]
            //    - CPNAME: ASCII 字符串，参数名称
            //    - CPVAL:  可以是任意 SECS 数据类型（A, U4, F8 等）
            var paramsContainer = root[1];
            if (paramsContainer.Format == SecsFormat.List)
            {
                // 遍历每个参数项
                foreach (var paramItem in paramsContainer.Items)
                {
                    // 每个参数必须是一个包含至少 2 个元素的 List: [CPNAME, CPVAL]
                    if (paramItem.Format != SecsFormat.List || paramItem.Count < 2)
                    {
                        // 格式异常，根据 GEM 标准可忽略该参数或抛出异常
                        // 此处选择跳过并继续解析其他参数
                        continue;
                    }

                    // 解析参数名 (CPNAME)
                    var nameItem = paramItem[0];
                    if (nameItem == null || nameItem.Format != SecsFormat.ASCII)
                        continue;  // 参数名无效，跳过

                    string paramName = nameItem.GetString();

                    // 解析参数值 (CPVAL)
                    // 注意：CPVAL 的类型不固定（可能是字符串、整数、浮点数等）
                    // 为了通用性，我们在此处不进行强类型转换，而是直接存储原始 Item 对象，
                    // 交由上层 Handler 根据具体的参数名来解析。
                    var valueItem = paramItem[1];
                    data.Parameters[paramName] = valueItem;   // 存储 Item 对象
                }
            }
            // 如果参数列表为空或不是 List 类型，则 Parameters 保持为空字典，这是合法的。

            return data;
        }
    }
}
