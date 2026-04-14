using Secs4Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Secs4Net.Item;

namespace SECSbuilder
{
    /// <summary>
    /// S2F41 (Remote Command) 消息的构建器。
    /// 用于设备模拟主机发送远程命令（通常用于测试，或设备作为主动方时）。
    /// </summary>
    public static class S2F41_builder
    {
        /// <summary>
        /// 构建一个 S2F41 消息。
        /// </summary>
        /// <param name="rcmd">远程命令名称，如 "START"。</param>
        /// <param name="parameters">可选参数，键为参数名 (CPNAME)，值为参数值 (CPVAL)。</param>
        /// <returns>可直接通过 SecsGem.SendAsync 发送的 SecsMessage 对象。</returns>
        public static SecsMessage Build(string rcmd, Dictionary<string, object>? parameters = null)
        {
            // 参数列表的 Item 容器
            var paramItems = new List<Item>();

            if (parameters != null && parameters.Count > 0)
            {
                foreach (var kvp in parameters)
                {
                    // 每个参数构建为一个 List: L( CPNAME, CPVAL )
                    // CPNAME 固定为 ASCII 字符串
                    Item cpnameItem = A(kvp.Key);
                    // CPVAL 需要根据值的类型转换成对应的 Item
                    Item cpvalItem = BuildValueItem(kvp.Value);

                    paramItems.Add(L(cpnameItem, cpvalItem));
                }
            }

            // 根列表: L( RCMD, L(参数列表) )
            // 参数列表可以为空（即 L(0)）
            var message = new SecsMessage(2, 41, replyExpected: true)   // W-bit = true，期待对方回复 S2F42
            {
                Name = "RemoteCommand",
                SecsItem = L(
                    A(rcmd),                 // RCMD
                    L(paramItems.ToArray())  // 参数列表（可能为空）
                )
            };
            return message;
        }

        /// <summary>
        /// 将 .NET 运行时对象转换为对应的 Secs4Net Item。
        /// 支持常见类型：string -> A, int -> I4, double -> F8, bool -> Boolean, byte[] -> B 等。
        /// </summary>
        /// <param name="value">参数值，类型不定。</param>
        /// <returns>转换后的 Item 对象。</returns>
        /// <exception cref="NotSupportedException">当遇到不支持的类型时抛出。</exception>
        private static Item BuildValueItem(object value)
        {
            if (value == null)
                return A("");   // 空字符串作为占位

            Type type = value.GetType();

            // 处理基本类型
            if (type == typeof(string))
                return A((string)value);
            if (type == typeof(int) || type == typeof(uint))
                return I4(Convert.ToInt32(value));
            if (type == typeof(long) || type == typeof(ulong))
                return I8(Convert.ToInt64(value));
            if (type == typeof(short) || type == typeof(ushort))
                return I2(Convert.ToInt16(value));
            if (type == typeof(sbyte) || type == typeof(byte))
                return I1(Convert.ToSByte(value));
            if (type == typeof(float))
                return F4((float)value);
            if (type == typeof(double))
                return F8((double)value);
            if (type == typeof(bool))
                return Boolean((bool)value);
            if (type == typeof(byte[]))
                return B((byte[])value);
            if (type.IsEnum)
                return I4(Convert.ToInt32(value));

            // 对于复杂对象，可扩展为递归处理或序列化为字符串
            throw new NotSupportedException($"Unsupported parameter value type: {type.FullName}");
        }
    }
}
