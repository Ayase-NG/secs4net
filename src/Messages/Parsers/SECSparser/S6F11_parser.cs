using Secs4Net;
using SECSdata;

namespace SECSparser
{
    /// <summary>
    /// S6F11 Event Report Send 消息解析器。
    /// </summary>
    public static class S6F11_parser
    {
        public static S6F11_data Parse(SecsMessage msg)
        {
            if (msg.S != 6 || msg.F != 11)
                throw new ArgumentException($"Invalid message type. Expected S6F11, but got S{msg.S}F{msg.F}.");

            var root = msg.SecsItem;
            if (root == null || root.Format != SecsFormat.List || root.Count < 3)
                throw new InvalidOperationException("Invalid S6F11 message format: root list missing or invalid.");

            var dataId = root[0].GetUIntId("DATA");
            if (dataId > byte.MaxValue)
                throw new InvalidOperationException("Invalid S6F11 message format: DATAID out of byte range.");

            var data = new S6F11_data
            {
                DATAID = (byte)dataId,
                CEID = root[1].GetUIntId("CE"),
                timeStamp = DateTime.UtcNow
            };

            var reports = root[2];
            if (reports.Format != SecsFormat.List)
                throw new InvalidOperationException("Invalid S6F11 message format: report list not found.");

            foreach (var reportItem in reports.Items)
            {
                if (reportItem.Format != SecsFormat.List || reportItem.Count < 2)
                    continue;

                uint rptId;
                try
                {
                    rptId = reportItem[0].GetUIntId("RPT");
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                var values = new List<S6F11_name_value_data>();
                var valueList = reportItem[1];
                if (valueList.Format == SecsFormat.List)
                {
                    foreach (var v in valueList.Items)
                    {
                        if (v.Format == SecsFormat.List && v.Count >= 2 && v[0].Format == SecsFormat.ASCII)
                        {
                            values.Add(new S6F11_name_value_data
                            {
                                CName = v[0].GetString() ?? string.Empty,
                                CValue = v[1]
                            });
                        }
                    }
                }

                data.Reports.Add(new S6F11_report_data
                {
                    RPTID = rptId,
                    Values = values
                });
            }

            return data;
        }
    }
}
