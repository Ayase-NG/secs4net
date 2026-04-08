using Secs4Net;
using SECSdata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SECSbuilder
{
    /// <summary>
    /// S2F38 "Enable Event Report Acknowledge" 消息的构建器。
    /// 用于设备回复主机的 S2F37 指令。
    /// </summary>
    public static class S2F38_builder
    {
        /// <summary>
        /// 根据确认码构造 S2F38 回复消息。
        /// </summary>
        /// <param name="eac">启用确认码 (EAC)。0 表示成功，其他值表示失败。</param>
        /// <returns>可发送的 SecsMessage 对象。</returns>
        public static SecsMessage Build(byte eac)
        {
            return new SecsMessage(2, 38, replyExpected: false)
            {
                Name = "EnableEventReportAcknowledge",
                SecsItem = Item.B(new byte[] { eac })   // 消息体仅包含一个字节的 EAC
            };
        }

        /// <summary>
        /// 从 S2F38_Data 数据对象构造 S2F38 回复消息。
        /// </summary>
        /// <param name="data">包含 EAC 值的数据对象。</param>
        /// <returns>可发送的 SecsMessage 对象。</returns>
        public static SecsMessage Build(S2F38_data data)
        {
            if (data == null)
                throw new System.ArgumentNullException(nameof(data));
            return Build(data.EAC);
        }
    }
}
