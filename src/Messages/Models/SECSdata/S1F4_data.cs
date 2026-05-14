using System;
using System.Collections.Generic;

namespace SECSdata
{
    /// <summary>
    /// S1F4 "Selected Equipment Status Data" 消息数据模型。
    /// 常见结构：L[n] { SV... }，其中每一项与 S1F3 请求中的 SVID 顺序一一对应。
    /// SML样例：<L [2] <A "RUN"> <U4 1200> >。
    /// </summary>
    public class S1F4_data
    {
        /// <summary>
        /// 状态变量值列表。
        /// 说明：为便于内部追踪，这里同时保留 SVID 与 SV 值。
        /// </summary>
        public List<S1F4_status_data> StatusList { get; set; } = new();

        /// <summary>
        /// 是否为空响应（没有任何状态变量值）。
        /// </summary>
        public bool IsEmpty => StatusList.Count == 0;

        /// <summary>
        /// 时间戳，用于内部记录发送时间和打印日志，单位为 UTC 时间。
        /// </summary>
        public DateTime timeStamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 新增或更新单个状态变量值。
        /// </summary>
        /// <param name="svid">状态变量 ID。</param>
        /// <param name="svValue">状态变量值（内部统一按字符串承载）。</param>
        public void AddOrUpdateStatus(uint svid, string svValue)
        {
            // if 关键分支：过滤非法 SVID（0 在当前约定中视为无效编号）。
            if (svid == 0)
            {
                return;
            }

            // for 关键分支：遍历现有列表，命中相同 SVID 时执行更新。
            for (var i = 0; i < StatusList.Count; i++)
            {
                // if 关键分支：找到目标 SVID 后更新并提前返回，避免重复项。
                if (StatusList[i].SVID == svid)
                {
                    StatusList[i].SV = svValue;
                    return;
                }
            }

            // 方法主流程：若未命中旧项，则追加新状态项。
            StatusList.Add(new S1F4_status_data
            {
                SVID = svid,
                SV = svValue
            });
        }

        /// <summary>
        /// 按 SVID 获取状态变量值。
        /// </summary>
        /// <param name="svid">状态变量 ID。</param>
        /// <returns>命中返回对应值，未命中返回空字符串。</returns>
        public string GetStatusValue(uint svid)
        {
            // for 关键分支：按顺序查找目标 SVID。
            for (var i = 0; i < StatusList.Count; i++)
            {
                // if 关键分支：找到匹配项后立即返回，减少不必要遍历。
                if (StatusList[i].SVID == svid)
                {
                    return StatusList[i].SV;
                }
            }

            // 方法兜底分支：当列表中不存在目标 SVID 时返回空字符串。
            return string.Empty;
        }
    }

    /// <summary>
    /// S1F4 中的单个状态变量数据项。
    /// </summary>
    public class S1F4_status_data
    {
        /// <summary>
        /// 状态变量 ID（SVID）。
        /// </summary>
        public uint SVID { get; set; }

        /// <summary>
        /// 状态变量值（SV），使用字符串统一承载，便于日志与上层处理。
        /// </summary>
        public string SV { get; set; } = string.Empty;

        /// <summary>
        /// 按指定类型描述对 SV 做可读化输出。
        /// </summary>
        /// <param name="valueType">值类型描述，例如 "A"、"BOOL"、"U4" 等。</param>
        /// <returns>可读化后的字符串。</returns>
        public string ToReadableValue(string valueType)
        {
            // switch 关键分支：按 valueType 选择对应的可读化策略。
            switch (valueType)
            {
                case "A":
                    // case 分支：ASCII 文本直接返回。
                    return SV;

                case "BOOL":
                    // if 关键分支：布尔值兼容 "1"/"true" 两种真值输入。
                    if (SV == "1" || SV.Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        return "True";
                    }

                    // BOOL 分支兜底：其余值按 False 处理。
                    return "False";

                default:
                    // default 分支：未知类型保持原始值，避免信息丢失。
                    return SV;
            }
        }
    }
}
