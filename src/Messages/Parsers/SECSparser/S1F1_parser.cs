using Secs4Net;
using SECSdata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SECSparser
{
    public static class S1F1_parser
    {
        /// <summary>
        /// 解析 S1F1 消息，返回强类型数据对象。
        /// </summary>
        /// <param name="msg">从 Secs4Net 接收到的 SecsMessage 对象。</param>
        /// <returns>解析后的 S1F1_Data 对象。</returns>
        /// <exception cref="ArgumentException">如果消息不是 S1F1，抛出异常。</exception>
        public static S1F1_data Parse(SecsMessage msg)
        {
            // 1. 校验消息类型
            if (msg.S != 1 || msg.F != 1)
            {
                throw new ArgumentException($"Invalid message type. Expected S1F1, but got S{msg.S}F{msg.F}.");
            }

            // 2. 可选：校验消息体是否为空（标准 S1F1 无数据体）
            //    注意：某些实现可能发送空列表或不发送任何 Item，这里做兼容处理。
            if (msg.SecsItem != null && msg.SecsItem.Format != SecsFormat.List)
            {
                // 记录警告日志，但不抛出异常，因为可能有些设备会附带额外数据
                // 根据标准，应忽略非预期的数据。
                Console.WriteLine($"Warning: S1F1 message body is not a List, format={msg.SecsItem.Format}. Ignoring.");
            }

            // 3. 创建数据对象（可携带元信息，如接收时间）
            var data = new S1F1_data
            {
                timeStamp = DateTime.UtcNow,
                // 如果需要，也可以从消息头中提取 SystemBytes 等信息
                // SystemBytes = msg.SystemBytes
            };

            return data;
        }
    }
}
