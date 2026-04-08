using Secs4Net;
using SECSdata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SECSparser
{
    public static class S2F34_parser
    {
        public static S2F34_data Parse(SecsMessage msg)
        {
            // 1. 验证消息类型
            if (msg.S != 2 || msg.F != 34)
                throw new ArgumentException($"Invalid message type. Expected S2F34, but got S{msg.S}F{msg.F}.");

            // 2. 验证消息体
            var root = msg.SecsItem;
            if (root == null || root.Format != SecsFormat.Binary || root.Count < 1)
                throw new InvalidOperationException("Invalid S2F34 message format: missing DRACK data.");

            // 3. 解析 DRACK
            byte drack = root.FirstValueOrDefault<byte>(2); // 默认值为2 (格式错误)

            return new S2F34_data
            {
                DRACK = drack,
                timeStamp = DateTime.UtcNow
            };
        }
    }
}
