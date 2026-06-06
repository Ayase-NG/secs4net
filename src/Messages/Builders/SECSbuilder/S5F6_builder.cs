using Secs4Net;
using SECSdata;
using static Secs4Net.Item;

namespace SECSbuilder
{
    /// <summary>
    /// S5F6 报警列表应答消息构建器。
    /// </summary>
    public static class S5F6_builder
    {
        public static SecsMessage Build(S5F6_data data)
        {
            ArgumentNullException.ThrowIfNull(data);

            var alarmItems = data.Alarms
                .Select(a => L(B(a.ALCD), U4(a.ALID), A(a.ALTX ?? string.Empty)))
                .ToArray();

            return new SecsMessage(5, 6, replyExpected: false)
            {
                Name = "ListAlarmReply",
                SecsItem = L(alarmItems)
            };
        }
    }
}
