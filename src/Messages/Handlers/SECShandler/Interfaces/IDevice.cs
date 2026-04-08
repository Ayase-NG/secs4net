using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SECShandler.Interfaces
{
    /// <summary>
    /// 设备业务接口，用于获取设备状态和信息。
    /// 具体实现由主程序提供，与通信层解耦。
    /// </summary>
    public interface IDevice
    {
        /// <summary>设备是否在线（可接受远程控制）</summary>
        bool IsOnline { get; }

        /// <summary>设备型号（MDLN），例如 "EFEM-2000"</summary>
        string ModelNumber { get; }

        /// <summary>软件版本（SOFTREV），例如 "1.0.0"</summary>
        string SoftwareRevision { get; }
    }
}
