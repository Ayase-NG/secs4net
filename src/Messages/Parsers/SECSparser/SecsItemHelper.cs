using Secs4Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SECSparser
{
    /// <summary>
    /// 工具类，给parser提供通用解析方法来进行匹配
    /// </summary>
    public static class SecsItemHelper
    {
        /// <summary>
        /// 通用获取 ID 的方法，
        /// 仅接受 U1/U2/U4 格式，从 Item 中提取无符号整数值；如果类型不匹配则抛出异常（用于上层返回错误码）。
        /// </summary>
        /// <param name="item">要提取的 Item</param>
        /// <param name="name">用来识别通用方法时不同的类型，提供给log使用</param>
        /// <returns>提取的 uint 值</returns>
        /// <exception cref="InvalidOperationException">当 item 类型不是 U1/U2/U4 时抛出，错误信息为 "ID type error. Expected U1/U2/U4."</exception>
        public static uint GetUIntId(this Item item,string name)
        {
            if (item == null)
                throw new InvalidOperationException(name + "ID type error. Expected U1/U2/U4.");

            switch (item.Format)
            {
                case SecsFormat.U1:
                    return item.FirstValueOrDefault<byte>(0);
                case SecsFormat.U2:
                    return item.FirstValueOrDefault<ushort>(0);
                case SecsFormat.U4:
                    return item.FirstValueOrDefault<uint>(0);
                default:
                    throw new InvalidOperationException(name + "ID type error. Expected U1/U2/U4.");
            }
        }
    }
}
