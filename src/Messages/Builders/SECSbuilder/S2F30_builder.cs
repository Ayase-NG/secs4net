using Secs4Net;
using SECSdata;
using System.Globalization;
using static Secs4Net.Item;

namespace SECSbuilder
{
    /// <summary>
    /// S2F30 "Equipment Constant Namelist" 消息构建器。
    /// 常见结构：L[n] { L[6] { ECID, ECNAME, ECMIN, ECMAX, ECDEF, UNITS } ... }。
    /// </summary>
    public static class S2F30_builder
    {
        /// <summary>
        /// 根据设备常量定义列表构建 S2F30 消息。
        /// </summary>
        /// <param name="constantList">设备常量定义列表。</param>
        /// <returns>可发送的 SecsMessage。</returns>
        public static SecsMessage Build(IEnumerable<S2F30_constant_data>? constantList = null)
        {
            var constantItems = new List<Item>();

            // for each 关键分支：按请求顺序输出每个设备常量的六项定义信息。
            if (constantList is not null)
            {
                foreach (var constant in constantList)
                {
                    // if 关键分支：忽略空项，避免构建无效的列表元素。
                    if (constant is null)
                    {
                        continue;
                    }

                    constantItems.Add(L(
                        U4(constant.ECID),
                        A(constant.ECNAME),
                        BuildValueItem(constant.Format, constant.ECMIN),
                        BuildValueItem(constant.Format, constant.ECMAX),
                        BuildValueItem(constant.Format, constant.ECDEF),
                        A(constant.UNITS)
                    ));
                }
            }

            return new SecsMessage(2, 30, replyExpected: false)
            {
                Name = "EquipmentConstantNamelist",
                SecsItem = L(constantItems.ToArray())
            };
        }

        /// <summary>
        /// 通过数据模型构建 S2F30 消息。
        /// </summary>
        /// <param name="data">S2F30 数据模型。</param>
        /// <returns>可发送的 SecsMessage。</returns>
        public static SecsMessage Build(S2F30_data data)
        {
            ArgumentNullException.ThrowIfNull(data);
            return Build(data.ConstantList);
        }

        /// <summary>
        /// 按配置的 SECS-II 格式构建设备常量值，空值或无效值生成对应格式的空 Item。
        /// </summary>
        /// <param name="format">SECS-II 格式名称。</param>
        /// <param name="value">待编码的文本值。</param>
        /// <returns>与格式匹配的 Item。</returns>
        private static Item BuildValueItem(string? format, string? value)
        {
            var normalizedFormat = string.IsNullOrWhiteSpace(format)
                ? "A"
                : format.Trim().ToUpperInvariant();
            var normalizedValue = value?.Trim() ?? string.Empty;

            // switch 关键分支：数值解析失败时返回同格式空 Item，避免伪造零值。
            return normalizedFormat switch
            {
                "U1" => byte.TryParse(normalizedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u1) ? U1(u1) : U1(),
                "U2" => ushort.TryParse(normalizedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u2) ? U2(u2) : U2(),
                "U4" => uint.TryParse(normalizedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u4) ? U4(u4) : U4(),
                "U8" => ulong.TryParse(normalizedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u8) ? U8(u8) : U8(),
                "I1" => sbyte.TryParse(normalizedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i1) ? I1(i1) : I1(),
                "I2" => short.TryParse(normalizedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i2) ? I2(i2) : I2(),
                "I4" => int.TryParse(normalizedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i4) ? I4(i4) : I4(),
                "I8" => long.TryParse(normalizedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i8) ? I8(i8) : I8(),
                "F4" => float.TryParse(normalizedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var f4) ? F4(f4) : F4(),
                "F8" => double.TryParse(normalizedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var f8) ? F8(f8) : F8(),
                "B" or "BINARY" => byte.TryParse(normalizedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var binary) ? B(binary) : B(),
                "BOOLEAN" or "BOOL" => TryParseBoolean(normalizedValue, out var boolean) ? Boolean(boolean) : Boolean(),
                "J" or "JIS8" => J(normalizedValue),
                "L" or "LIST" => L(),
                _ => A(normalizedValue)
            };
        }

        /// <summary>
        /// 将标准布尔文本或 0/1 文本解析为布尔值。
        /// </summary>
        /// <param name="value">待解析文本。</param>
        /// <param name="result">解析后的布尔值。</param>
        /// <returns>是否解析成功。</returns>
        private static bool TryParseBoolean(string value, out bool result)
        {
            // if 关键分支：优先支持标准 True/False 文本。
            if (bool.TryParse(value, out result))
            {
                return true;
            }

            // if 关键分支：兼容 SECS 配置中常用的 0/1 布尔表示。
            if (value == "1" || value == "0")
            {
                result = value == "1";
                return true;
            }

            result = false;
            return false;
        }
    }
}
