using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SECShandler.Interfaces
{
    /// <summary>
    /// 设备运行状态。
    /// </summary>
    public enum DeviceRunStatus
    {
        // 未知状态，初始值
        Unknown,
        // 设备正在初始化，尚未准备好接受远程控制命令
        Initializing,
        // 设备空闲，已准备好接受远程控制命令
        Idel,
        // 设备正在运行，接受远程控制命令可能会导致异常
        Running,
        // 设备报警，仅接收部分命令
        Jam
    }

    /// <summary>
    /// 设备业务接口，用于获取设备状态和信息。
    /// 具体实现由主程序提供，与通信层解耦。
    /// </summary>
    public interface IDevice
    {
        /// <summary>设备是否在线（可接受远程控制）</summary>
        bool IsOnline { get; set; }

        /// <summary>设备状态:unknown,initializing,idle,running</summary>
        string Status { get; set; }

        /// <summary>设备运行模式</summary>
        string Mode { get; set; }

        /// <summary>设备当前处理的晶圆槽位列表</summary>
        List<uint> SlotsList { get; set; }

        /// <summary>设备当前运行状态</summary>
        DeviceRunStatus RunStatus { get; }

        /// <summary>设备型号（MDLN），例如 "GWN-PW-001"</summary>
        string ModelNumber { get; }

        /// <summary>软件版本（SOFTREV），例如 "1.0.0"</summary>
        string SoftwareRevision { get; }

        Task StartProcessAsync(string? lotId);

        Task StopProcessAsync();

        Task PauseProcessAsync();

        Task ResumeProcessAsync();

        Task AbortProcessAsync();

        Task PPSelectAsync();

        Task ChangeToLocalAsync();

        Task LoadCarrierAsync();

        Task UnLoadCarrierAsync();
    }
}
