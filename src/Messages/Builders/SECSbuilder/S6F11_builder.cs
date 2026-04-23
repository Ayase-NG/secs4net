using Secs4Net;
using SECSdata;
using static Secs4Net.Item;

namespace SECSbuilder
{
    /// <summary>
    /// S6F11 Event Report Send 消息构建器。
    /// </summary>
    public static class S6F11_builder
    {
        public static SecsMessage Build(S6F11_data data)
        {
            ArgumentNullException.ThrowIfNull(data);

            var reportItems = new List<Item>();
            foreach (var report in data.Reports)
            {
                var valueItems = new List<Item>();
                foreach (var value in report.Values)
                {
                    valueItems.Add(BuildValueItem(value));
                }

                reportItems.Add(L(
                    U4(report.RPTID),
                    L(valueItems.ToArray())
                ));
            }

            return new SecsMessage(6, 11, replyExpected: true)
            {
                Name = "EventReportSend",
                SecsItem = L(
                    U1(data.DATAID),
                    U4(data.CEID),
                    L(reportItems.ToArray())
                )
            };
        }

        private static Item BuildValueItem(object? value)
        {
            if (value is null)
                return A(string.Empty);

            if (value is Item item)
                return item;

            var type = value.GetType();

            if (type == typeof(string)) return A((string)value);
            if (type == typeof(bool)) return Boolean((bool)value);
            if (type == typeof(byte)) return U1((byte)value);
            if (type == typeof(sbyte)) return I1((sbyte)value);
            if (type == typeof(short)) return I2((short)value);
            if (type == typeof(ushort)) return U2((ushort)value);
            if (type == typeof(int)) return I4((int)value);
            if (type == typeof(uint)) return U4((uint)value);
            if (type == typeof(long)) return I8((long)value);
            if (type == typeof(ulong)) return U8((ulong)value);
            if (type == typeof(float)) return F4((float)value);
            if (type == typeof(double)) return F8((double)value);
            if (type == typeof(byte[])) return B((byte[])value);

            if (type.IsEnum)
                return I4(Convert.ToInt32(value));

            throw new NotSupportedException($"Unsupported report value type: {type.FullName}");
        }
    }
}
