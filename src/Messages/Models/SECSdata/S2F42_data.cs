using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SECSdata
{
    public class S2F42_data
    {
        // 主机命令确认码 (HCACK)
        public byte HCACK { get; set; }
        // 当 HCACK=1 时，返回不支持的命令（ASCII 字符串）。当 HCACK=0 或 2 时，此字段可忽略或置空。
        public required string ErrorRCMD { get; set; }
        public bool IsSuccess => HCACK == 0;
    }
}
