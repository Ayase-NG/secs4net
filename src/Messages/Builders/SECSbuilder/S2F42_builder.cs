using Secs4Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Secs4Net.Item;
using SECSdata;

namespace SECSbuilder
{
    /// <summary>
    /// S2F42 "Remote Command Acknowledge" 消息的构建器。
    /// 设备通过此消息回复主机的 S2F41 远程命令。
    /// </summary>
    public static class S2F42_builder
    {
        /// <summary>
        /// 根据确认码和可选的错误参数名构建 S2F42 消息。
        /// </summary>
        /// <param name="hcack">命令确认码 (HCACK)，U4 类型：
        /// 0 = 接受并执行成功
        /// 1 = 命令不存在（可选，可附带不支持的参数名）
        /// 2 = 无法执行（如设备状态不允许）
        /// </param>
        /// <param name="errorParam">当 HCACK=1 时，可选返回不支持的参数名（ASCII 字符串）。</param>
        /// <returns>可发送的 SecsMessage 对象。</returns>
        public static SecsMessage Build(byte hcack, string? errorParam = null)
        {
            // 根据 SEMI E5 标准，S2F42 的消息体格式为：
            // <L [2]
            //   <U4 HCACK>
            //   <A [n] ERROR_PARAM?>
            // >
            // 其中 ERROR_PARAM 是可选的，当 HCACK=1 时可用于指示哪个参数无效。
            var message = new SecsMessage(2, 42, replyExpected: false)
            {
                Name = "RemoteCommandAcknowledge",
                SecsItem = L(
                    U4(hcack),                    // HCACK 确认码
                    A(errorParam ?? string.Empty) // 错误参数名，无则传空字符串
                )
            };
            return message;
        }

        /// <summary>
        /// 通过 S2F42_Data 数据对象构建消息（可选）。
        /// </summary>
        /// <param name="data">包含 HCACK 和 ErrorParam 的数据对象。</param>
        public static SecsMessage Build(S2F42_data data)
        {
            if (data == null)
                throw new System.ArgumentNullException(nameof(data));
            return Build(data.HCACK, data.ErrorRCMD);
        }
    }
}
