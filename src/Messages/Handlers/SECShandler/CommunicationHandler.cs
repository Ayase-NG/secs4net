using Secs4Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SECShandler
{
    /// <summary>
    /// 通信相关的消息处理器。
    /// 负责处理主机（Host）发来的通信建立和心跳检测消息，
    /// 并根据设备状态回复相应的响应消息。
    /// </summary>
    public class CommunicationHandler
    {
        private readonly SecsGem _secsGem;   // Secs4Net 的消息引擎，用于发送回复消息
        private readonly IDevice _device;    // 设备业务接口，获取设备信息及状态

        /// <summary>
        /// 构造函数，通过依赖注入获取通信引擎和设备业务接口。
        /// </summary>
        /// <param name="secsGem">Secs4Net 消息引擎实例</param>
        /// <param name="device">设备业务接口实例</param>
        public CommunicationHandler(SecsGem secsGem, IDevice device)
        {
            _secsGem = secsGem;
            _device = device;
        }

        /// <summary>
        /// 处理 S1F1 "Are You There Request" 消息。
        /// 主机发送此消息询问设备是否在线，设备需要回复 S1F2 携带型号和软件版本。
        /// </summary>
        public async Task HandleS1F1Async(Secs4Net.PrimaryMessageWrapper primary)
        {
            // 构造 S1F2 回复消息
            var reply = new SecsMessage(1, 2, replyExpected: false)
            {
                Name = "OnlineData",  // 消息名称（用于日志或调试）
                SecsItem = Item.L(
                    Item.A(_device.ModelNumber),      // MDLN: 设备型号
                    Item.A(_device.SoftwareRevision)  // SOFTREV: 软件版本
                )
            };
            // 使用 PrimaryMessageWrapper.TryReplyAsync 回复以保持相同的 System ID
            try
            {
                await primary.TryReplyAsync(reply).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 若无法使用 TryReplyAsync（例如 wrapper 不可用），退回到直接发送
                await _secsGem.SendAsync(reply).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 处理 S1F13 "Establish Communications Request" 消息。
        /// 主机请求与设备建立通信，设备需要根据自身状态回复 S1F14，
        /// 告知是否接受连接，并返回设备型号和软件版本。
        /// </summary>
        public async Task HandleS1F13Async(Secs4Net.PrimaryMessageWrapper primary)
        {
            // 根据设备是否在线决定是否接受通信请求
            bool accept = _device.IsOnline;

            // 构造 S1F14 回复消息
            var reply = new SecsMessage(1, 14, replyExpected: false)
            {
                Name = "EstablishCommunicationsAcknowledge",
                SecsItem = Item.L(
                    // COMMACK: 0 = 接受, 1 = 拒绝
                    Item.B(new byte[] { accept ? (byte)0 : (byte)1 }),
                    // 子列表：设备型号和软件版本
                    Item.L(
                        Item.A(_device.ModelNumber),
                        Item.A(_device.SoftwareRevision)
                    )
                )
            };
            try
            {
                await primary.TryReplyAsync(reply).ConfigureAwait(false);
            }
            catch (Exception)
            {
                await _secsGem.SendAsync(reply).ConfigureAwait(false);
            }
        }

        // 如果需要处理其他通信相关消息（例如 S1F14 作为设备主动发起连接时的回复），可以继续添加方法
        // public async Task HandleS1F14Async(S1F14_Data data) { ... }
    }
}
