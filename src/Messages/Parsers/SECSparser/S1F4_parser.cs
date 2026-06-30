using Secs4Net;
using SECSdata;

namespace SECSparser
{
    /// <summary>
    /// S1F4 消息解析器。
    /// 常见结构：L[n] { SV... }
    /// </summary>
    public static class S1F4_parser
    {
        /// <summary>
        /// 将原始 SecsMessage 解析为 S1F4_data 对象。
        /// </summary>
        /// <param name="msg">从 Secs4Net 接收到的 S1F4 消息。</param>
        /// <returns>解析后的强类型数据对象。</returns>
        /// <exception cref="ArgumentException">消息类型不是 S1F4 时抛出。</exception>
        /// <exception cref="InvalidOperationException">消息格式不符合预期时抛出。</exception>
        public static S1F4_data Parse(SecsMessage msg)
        {
            // if 关键分支：先校验消息头，必须是 S1F4。
            if (msg.S != 1 || msg.F != 4)
            {
                throw new ArgumentException($"Invalid message type. Expected S1F4, but got S{msg.S}F{msg.F}.");
            }

            // 初始化返回对象并记录解析时间。
            var data = new S1F4_data
            {
                timeStamp = DateTime.UtcNow
            };

            var root = msg.SecsItem;

            // if 关键分支：允许空消息体，按空状态列表返回。
            if (root is null)
            {
                return data;
            }

            // if 关键分支：根节点必须是 List，否则视为格式错误。
            if (root.Format != SecsFormat.List)
            {
                throw new InvalidOperationException("Invalid S1F4 message: root item is not a List.");
            }

            // for each 关键分支：逐项解析 SV，并按顺序生成内部状态项。
            foreach (var svItem in root.Items)
            {
                string svValue;

                // switch 关键分支：按 SecsFormat 执行对应的取值策略。
                switch (svItem.Format)
                {
                    case SecsFormat.ASCII:
                        // case 分支：ASCII 文本直接读取。
                        svValue = svItem.GetString() ?? string.Empty;
                        break;

                    case SecsFormat.Binary:
                        // case 分支：Binary 以十六进制字符串形式承载，便于日志追踪。
                        svValue = BitConverter.ToString(svItem.GetMemory<byte>().ToArray());
                        break;

                    case SecsFormat.U1:
                        svValue = svItem.FirstValueOrDefault<byte>(0).ToString();
                        break;

                    case SecsFormat.U2:
                        svValue = svItem.FirstValueOrDefault<ushort>(0).ToString();
                        break;

                    case SecsFormat.U4:
                        svValue = svItem.FirstValueOrDefault<uint>(0).ToString();
                        break;

                    case SecsFormat.I1:
                        svValue = svItem.FirstValueOrDefault<sbyte>(0).ToString();
                        break;

                    case SecsFormat.I2:
                        svValue = svItem.FirstValueOrDefault<short>(0).ToString();
                        break;

                    case SecsFormat.I4:
                        svValue = svItem.FirstValueOrDefault<int>(0).ToString();
                        break;

                    case SecsFormat.Boolean:
                        svValue = svItem.FirstValueOrDefault<bool>(false) ? "True" : "False";
                        break;

                    default:
                        // default 分支：未知类型降级为 ToString，避免解析链路中断。
                        svValue = svItem.ToString();
                        break;
                }

                // S1F4 中通常不显式携带 SVID，这里按顺序占位为 0。
                data.StatusList.Add(new S1F4_status_data
                {
                    SVID = 0,
                    SV = svValue
                });
            }

            return data;
        }
    }
}
