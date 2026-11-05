using System.Globalization;
using Secs4Net;

namespace SECSbuilder
{
    /// <summary>
    /// S2F18 Date and Time Data：以单个 ASCII TIME 返回设备当前时间。
    /// </summary>
    public static class S2F18_builder
    {
        /// <summary>
        /// 按项目 UTC 约定构建 A[16] yyyyMMddHHmmssff；不携带时区、列表或确认码。
        /// </summary>
        public static SecsMessage Build(DateTime equipmentTimeUtc)
        {
            // TIME 本身不带时区，仅接受明确的 UTC，避免隐式转换造成查询与校时不一致。
            if (equipmentTimeUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("Equipment time must be UTC.", nameof(equipmentTimeUtc));

            return new SecsMessage(2, 18, replyExpected: false)
            {
                Name = "DateAndTimeData",
                SecsItem = Item.A(equipmentTimeUtc.ToString("yyyyMMddHHmmssff", CultureInfo.InvariantCulture))
            };
        }
    }
}
