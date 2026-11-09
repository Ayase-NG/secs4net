using System;
using System.Collections.Generic;

namespace SECSdata
{
    /// <summary>
    /// S16F16 PRJobMultiCreateAck：作业标识列表及创建确认和错误明细。
    /// 结构：L[2] { L[n] { PRJOBID }, L[2] { Boolean ACKA, L[e] { L[2] { ERRCODE, ERRTEXT } } } }。
    /// </summary>
    public class S16F16_data
    {
        /// <summary>
        /// 本次请求中已确认存在或成功创建的 PJID，不填入未成功保存的标识。
        /// </summary>
        public List<string> PRJOBIDs { get; } = new();

        /// <summary>
        /// 创建确认：true=整批接受，false=拒绝或未全部创建成功；默认拒绝。
        /// </summary>
        public bool ACKA { get; set; }

        /// <summary>
        /// 错误明细；接受时为空，拒绝时至少提供一个错误。
        /// </summary>
        public List<S16F16_error_data> Errors { get; } = new();

        /// <summary>
        /// 保留内部 0=接受、1=拒绝的兼容访问方式；线上构建器编码 Boolean ACKA，不编码此字节。
        /// </summary>
        public byte ACK
        {
            get => ACKA ? (byte)0 : (byte)1;
            set
            {
                // 禁止把自定义字节值静默转换为标准布尔确认。
                if (value > 1)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "ACK must be 0 or 1.");
                ACKA = value == 0;
            }
        }

        /// <summary>
        /// 是否接受本次请求。
        /// </summary>
        public bool IsAccepted => ACKA;

        /// <summary>
        /// 时间戳，用于内部记录发送时间和打印日志，单位为 UTC 时间。
        /// </summary>
        public DateTime timeStamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// 创建错误明细；ERRCODE 使用 U2 编码，ERRTEXT 使用 ASCII，不额外改变线上错误项结构。
    /// </summary>
    public sealed class S16F16_error_data
    {
        /// <summary>
        /// 错误码；默认 1（通用未知错误），具体业务错误码须按采用的规范另行映射。
        /// </summary>
        public ushort ERRCODE { get; set; } = 1;

        /// <summary>
        /// 错误描述，可包含 PJID、字段名称及失败原因。
        /// </summary>
        public string ERRTEXT { get; set; } = string.Empty;
    }
}
