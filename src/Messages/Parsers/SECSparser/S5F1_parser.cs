using Secs4Net;
using SECSdata;
using System;

namespace SECSparser
{
    /// <summary>
    /// S5F1 "Alarm Report Send (ARS)" 消息解析器。
    /// 标准消息体格式：L[3] { ALCD(B/U1), ALID(Ux/Ix), ALTX(A) }。
    /// </summary>
    public static class S5F1_parser
    {
        /// <summary>
        /// 将原始 <see cref="SecsMessage"/> 解析为 <see cref="S5F1_data"/>。
        /// </summary>
        /// <param name="msg">从 Secs4Net 接收到的消息。</param>
        /// <returns>解析后的强类型数据对象。</returns>
        /// <exception cref="ArgumentException">消息类型不是 S5F1 时抛出。</exception>
        /// <exception cref="InvalidOperationException">消息体结构或字段类型不符合预期时抛出。</exception>
        public static S5F1_data Parse(SecsMessage msg)
        {
            if (msg.S != 5 || msg.F != 1)
                throw new ArgumentException($"Invalid message type. Expected S5F1, but got S{msg.S}F{msg.F}.");

            var root = msg.SecsItem;
            if (root is null || root.Format != SecsFormat.List || root.Count < 3)
                throw new InvalidOperationException("Invalid S5F1 message format: expected root L[3] { ALCD, ALID, ALTX }.");

            var data = new S5F1_data
            {
                ALCD = ReadAlarmCode(root[0]),
                ALID = ReadAlarmId(root[1]),
                ALTX = ReadAlarmText(root[2]),
                timeStamp = DateTime.UtcNow
            };

            return data;
        }

        /// <summary>
        /// 读取 ALCD（报警代码）。
        /// 支持 Binary/U1 的 1 字节值。
        /// </summary>
        private static byte ReadAlarmCode(Item item)
        {
            if (item is null)
                throw new InvalidOperationException("Invalid S5F1 message: ALCD not found.");

            return item.Format switch
            {
                SecsFormat.Binary => item.FirstValueOrDefault<byte>(0),
                SecsFormat.U1 => item.FirstValueOrDefault<byte>(0),
                _ => throw new InvalidOperationException("Invalid S5F1 message: ALCD type error. Expected B/U1.")
            };
        }

        /// <summary>
        /// 读取 ALID（报警编号）。
        /// 支持 U1/U2/U4 以及 I1/I2/I4（负数视为非法）。
        /// </summary>
        private static uint ReadAlarmId(Item item)
        {
            if (item is null)
                throw new InvalidOperationException("Invalid S5F1 message: ALID not found.");

            return item.Format switch
            {
                SecsFormat.U1 => item.FirstValueOrDefault<byte>(0),
                SecsFormat.U2 => item.FirstValueOrDefault<ushort>(0),
                SecsFormat.U4 => item.FirstValueOrDefault<uint>(0),
                SecsFormat.I1 => ReadSigned(item.FirstValueOrDefault<sbyte>(0)),
                SecsFormat.I2 => ReadSigned(item.FirstValueOrDefault<short>(0)),
                SecsFormat.I4 => ReadSigned(item.FirstValueOrDefault<int>(0)),
                _ => throw new InvalidOperationException("Invalid S5F1 message: ALID type error. Expected U1/U2/U4/I1/I2/I4.")
            };
        }

        /// <summary>
        /// 读取 ALTX（报警文本）。
        /// 按标准优先使用 ASCII，其他类型按字符串兜底。
        /// </summary>
        private static string ReadAlarmText(Item item)
        {
            if (item is null)
                return string.Empty;

            if (item.Format == SecsFormat.ASCII)
                return item.GetString() ?? string.Empty;

            return item.ToString() ?? string.Empty;
        }

        private static uint ReadSigned(int value)
        {
            if (value < 0)
                throw new InvalidOperationException("Invalid S5F1 message: ALID cannot be negative.");

            return (uint)value;
        }
    }
}
