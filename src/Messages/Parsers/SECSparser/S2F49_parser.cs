using Secs4Net;
using SECSdata;
using System;

namespace SECSparser
{
    /// <summary>
    /// S2F49 增强远程命令消息解析器。
    /// </summary>
    public static class S2F49_parser
    {
        /// <summary>
        /// 按 DATAID、OBJSPEC、RCMD 和参数列表结构解析 S2F49 消息。
        /// </summary>
        /// <param name="msg">待解析的 S2F49 SECS 消息。</param>
        /// <returns>包含命令信息和原始 CPVAL Item 的强类型数据。</returns>
        /// <exception cref="ArgumentNullException">消息为空时抛出。</exception>
        /// <exception cref="ArgumentException">消息不是 S2F49 时抛出。</exception>
        /// <exception cref="InvalidOperationException">消息数据结构或字段类型无效时抛出。</exception>
        public static S2F49_data Parse(SecsMessage msg)
        {
            ArgumentNullException.ThrowIfNull(msg);

            // 仅允许解析 S2F49，避免错误消息进入增强远程命令处理流程。
            if (msg.S != 2 || msg.F != 49)
            {
                throw new ArgumentException(
                    $"Invalid message type. Expected S2F49, but got S{msg.S}F{msg.F}.",
                    nameof(msg));
            }

            // 标准 S2F49 根节点必须为 L[4]：DATAID、OBJSPEC、RCMD、参数列表。
            Item root = msg.SecsItem
                ?? throw new InvalidOperationException("Invalid S2F49 message: root list is missing.");
            if (root.Format != SecsFormat.List || root.Count != 4)
            {
                throw new InvalidOperationException("Invalid S2F49 message: expected a root list containing exactly 4 items.");
            }

            // DATAID 按项目统一约定接受 U1/U2/U4，并转换为 uint。
            uint dataId = root[0].GetUIntId("DATA");

            // OBJSPEC 必须为 ASCII；允许空字符串表示未指定对象。
            Item objectSpecItem = root[1];
            if (objectSpecItem.Format != SecsFormat.ASCII)
            {
                throw new InvalidOperationException("Invalid S2F49 message: OBJSPEC must be an ASCII item.");
            }

            // RCMD 必须是非空 ASCII 命令名称。
            Item remoteCommandItem = root[2];
            if (remoteCommandItem.Format != SecsFormat.ASCII)
            {
                throw new InvalidOperationException("Invalid S2F49 message: RCMD must be an ASCII item.");
            }

            string remoteCommand = remoteCommandItem.GetString();
            if (string.IsNullOrWhiteSpace(remoteCommand))
            {
                throw new InvalidOperationException("Invalid S2F49 message: RCMD must not be empty.");
            }

            Item parameterList = root[3];
            if (parameterList.Format != SecsFormat.List)
            {
                throw new InvalidOperationException("Invalid S2F49 message: command parameters must be a list.");
            }

            var data = new S2F49_data
            {
                DATAID = dataId,
                OBJSPEC = objectSpecItem.GetString(),
                RCMD = remoteCommand
            };

            // 每个参数必须采用 L[2] { CPNAME(A), CPVAL(任意 SECS 类型) } 结构。
            foreach (Item parameterItem in parameterList.Items)
            {
                if (parameterItem.Format != SecsFormat.List || parameterItem.Count != 2)
                {
                    throw new InvalidOperationException("Invalid S2F49 message: each command parameter must be a list containing exactly 2 items.");
                }

                Item parameterNameItem = parameterItem[0];
                if (parameterNameItem.Format != SecsFormat.ASCII)
                {
                    throw new InvalidOperationException("Invalid S2F49 message: CPNAME must be an ASCII item.");
                }

                string parameterName = parameterNameItem.GetString();
                if (string.IsNullOrWhiteSpace(parameterName))
                {
                    throw new InvalidOperationException("Invalid S2F49 message: CPNAME must not be empty.");
                }

                // 保留 CPVAL 原始 Item，交由上层根据命令和参数名称解析具体类型。
                data.Parameters[parameterName] = parameterItem[1];
            }

            return data;
        }
    }
}
