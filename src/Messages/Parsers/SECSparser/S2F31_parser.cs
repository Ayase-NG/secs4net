using Secs4Net;
using SECSdata;
using System.Globalization;

namespace SECSparser
{
    /// <summary>
    /// S2F31 时间同步请求解析器。
    /// </summary>
    public static class S2F31_parser
    {
        /// <summary>
        /// 校验 S2F31 的 ASCII TIME；无时区字段时按约定时区解释，默认沿用项目 UTC 约定。
        /// </summary>
        public static S2F31_data Parse(SecsMessage msg, TimeZoneInfo? timeZone = null)
        {
            ArgumentNullException.ThrowIfNull(msg);
            // 仅解析日期时间设置请求，避免误用其他消息。
            if (msg.S != 2 || msg.F != 31)
                throw new ArgumentException($"Invalid message type. Expected S2F31, but got S{msg.S}F{msg.F}.");

            var item = msg.SecsItem;
            // TIME 必须是单个 ASCII 数据项，不能使用列表或其他类型。
            if (item is null || item.Format != SecsFormat.ASCII)
                throw new InvalidOperationException("Invalid S2F31: time text should be ASCII.");

            var raw = item.GetString() ?? string.Empty;
            var utc = ParseToUtc(raw, timeZone ?? TimeZoneInfo.Utc);

            return new S2F31_data
            {
                TimeText = raw,
                HostTimeUtc = utc
            };
        }

        /// <summary>
        /// 严格解析 SECS 12/16 位 TIME；两位年份使用 1990–2089 窗口，拒绝无效或歧义本地时间。
        /// </summary>
        private static DateTime ParseToUtc(string text, TimeZoneInfo timeZone)
        {
            // 不修剪空白，不接受 ISO8601、14/17 位时间或非 ASCII 数字。
            if (text.Length is not (12 or 16) || text.Any(c => c is < '0' or > '9'))
                throw new InvalidOperationException("Invalid S2F31 TIME: expected 12 or 16 ASCII digits.");

            var format = "yyyyMMddHHmmssff";
            // 显式展开两位年份，不依赖操作系统 Calendar.TwoDigitYearMax。
            if (text.Length == 12)
            {
                text = (int.Parse(text.AsSpan(0, 2), CultureInfo.InvariantCulture) >= 90 ? "19" : "20") + text;
                format = "yyyyMMddHHmmss";
            }

            // 严格验证闰年、日期、时分秒和百分之一秒精度。
            if (!DateTime.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                throw new InvalidOperationException($"Invalid S2F31 time value: {text}");

            // TIME 不含 UTC 偏移，夏令时歧义不能静默选择一个时间。
            if (timeZone.IsInvalidTime(parsed) || timeZone.IsAmbiguousTime(parsed))
                throw new InvalidOperationException("Invalid S2F31 TIME: invalid or ambiguous time in the configured time zone.");

            return TimeZoneInfo.ConvertTimeToUtc(parsed, timeZone);
        }
    }
}
