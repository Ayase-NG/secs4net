using Secs4Net;
using SECSdata;

namespace SECSparser
{
    /// <summary>
    /// S5F5 报警列表查询请求解析器。
    /// </summary>
    public static class S5F5_parser
    {
        public static S5F5_data Parse(SecsMessage msg)
        {
            if (msg.S != 5 || msg.F != 5)
                throw new ArgumentException($"Invalid message type. Expected S5F5, but got S{msg.S}F{msg.F}.");

            var root = msg.SecsItem;
            if (root is null)
            {
                return new S5F5_data();
            }

            if (root.Format != SecsFormat.List)
                throw new InvalidOperationException("Invalid S5F5 message: expected list body.");

            var data = new S5F5_data();

            // 标准最小兼容：L[0] 或 L[1]{ALIDLIST}
            if (root.Count == 0)
            {
                return data;
            }

            var list = root[0];
            if (list.Format != SecsFormat.List)
                throw new InvalidOperationException("Invalid S5F5 ALID list type.");

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
