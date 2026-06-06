using Secs4Net;
using SECSdata;

namespace SECSparser
{
    /// <summary>
    /// S5F3 报警使能请求解析器。
    /// </summary>
    public static class S5F3_parser
    {
        public static S5F3_data Parse(SecsMessage msg)
        {
            if (msg.S != 5 || msg.F != 3)
                throw new ArgumentException($"Invalid message type. Expected S5F3, but got S{msg.S}F{msg.F}.");

            var root = msg.SecsItem;
            if (root is null || root.Format != SecsFormat.List || root.Count < 2)
                throw new InvalidOperationException("Invalid S5F3 message: expected L[2]{ALED,ALIDLIST}.");

            var data = new S5F3_data
            {
                ALED = root[0].Format switch
                {
                    SecsFormat.Binary => root[0].FirstValueOrDefault<byte>(0),
                    SecsFormat.U1 => root[0].FirstValueOrDefault<byte>(0),
                    _ => throw new InvalidOperationException("Invalid S5F3 ALED type.")
                }
            };

            var list = root[1];
            if (list.Format != SecsFormat.List)
                throw new InvalidOperationException("Invalid S5F3 ALIDLIST type.");

            foreach (var item in list.Items)
            {
                var alid = item.GetUIntId("AL");
                if (alid != 0)
                {
                    data.ALIDList.Add(alid);
                }
            }

            return data;
        }
    }
}
