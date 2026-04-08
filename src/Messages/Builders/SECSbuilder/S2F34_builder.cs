using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Secs4Net;
using SECSdata;

namespace SECSbuilder
{
    /// <summary>
    /// S2F34 "Define Report Acknowledge" 消息的构建器。
    /// 用于设备回复主机的 S2F33 指令。
    /// </summary>
    public static class S2F34_builder
    {
        /// <summary>
        /// 根据确认码构造 S2F34 回复消息。
        /// </summary>
        /// <param name="drack">定义报告确认码 (DRACK)。0 表示成功，其他值表示失败。</param>
        /// <returns>可发送的 SecsMessage 对象。</returns>
        public static SecsMessage Build(byte drack)
        {
            return new SecsMessage(2, 34, replyExpected: false)
            {
                Name = "DefineReportAcknowledge",
                SecsItem = Item.B(new byte[] { drack })  // 消息体仅包含一个字节的 DRACK
            };
        }

        /// <summary>
        /// 从 S2F34_Data 数据对象构造 S2F34 回复消息。
        /// </summary>
        /// <param name="data">包含 DRACK 值的数据对象。</param>
        /// <returns>可发送的 SecsMessage 对象。</returns>
        public static SecsMessage Build(S2F34_data data)
        {
            if (data == null)
                throw new System.ArgumentNullException(nameof(data));
            return Build(data.DRACK);
        }
    }
}
