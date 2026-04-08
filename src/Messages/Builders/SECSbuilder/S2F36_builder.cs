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
    /// S2F36 "Link Event Report Acknowledge" 消息的构建器。
    /// 用于设备回复主机的 S2F35 指令。
    /// </summary>
    public static class S2F36_builder
    {
        /// <summary>
        /// 根据确认码构造 S2F36 回复消息。
        /// </summary>
        /// <param name="lrack">链接确认码 (LRACK)。0 表示成功，其他值表示失败。</param>
        /// <returns>可发送的 SecsMessage 对象。</returns>
        public static SecsMessage Build(byte lrack)
        {
            return new SecsMessage(2, 36, replyExpected: false)
            {
                Name = "LinkEventReportAcknowledge",
                SecsItem = Item.B(new byte[] { lrack })   // 消息体仅包含一个字节的 LRACK
            };
        }

        /// <summary>
        /// 从 S2F36_Data 数据对象构造 S2F36 回复消息。
        /// </summary>
        /// <param name="data">包含 LRACK 值的数据对象。</param>
        /// <returns>可发送的 SecsMessage 对象。</returns>
        public static SecsMessage Build(S2F36_data data)
        {
            if (data == null)
                throw new System.ArgumentNullException(nameof(data));
            return Build(data.LRACK);
        }
    }
}
