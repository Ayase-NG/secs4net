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
                    // 关键分支：上报采用 VID+CPVal 结构，CPName 不参与编码。
                    valueItems.Add(L(
                        U2(value.VID),
                        A(value.CPVal ?? string.Empty)));
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
    }
}
