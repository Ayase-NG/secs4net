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
        public static S2F31_data Parse(SecsMessage msg)
        {
            if (msg.S != 2 || msg.F != 31)
                throw new ArgumentException($"Invalid message type. Expected S2F31, but got S{msg.S}F{msg.F}.");

            var item = msg.SecsItem;
            if (item is null || item.Format != SecsFormat.ASCII)
                throw new InvalidOperationException("Invalid S2F31: time text should be ASCII.");

            var raw = item.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException("Invalid S2F31: empty time text.");

            var utc = ParseToUtc(raw.Trim());

            return new S2F31_data
            {
                TimeText = raw,
                HostTimeUtc = utc
            };
        }

        private static DateTime ParseToUtc(string text)
        {
            // 常见 GEM 时间字符串兼容：yyyyMMddHHmmss / yyyyMMddHHmmssff / ISO8601
            var formats = new[]
            {
                "yyyyMMddHHmmss",
                "yyyyMMddHHmmssff",
                "yyyyMMddHHmmssfff",
                "yyyy-MM-ddTHH:mm:ss",
                "yyyy-MM-ddTHH:mm:ssZ",
                "yyyy-MM-ddTHH:mm:ss.fffZ"
            };

            if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                return parsed;
            }

            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
            {
                return parsed;
            }

            throw new InvalidOperationException($"Invalid S2F31 time format: {text}");
        }
    }
}
