using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SECShandler.Interfaces
{
    /// <summary>
    /// 报告存储接口，用于管理 RPTID 与 VID 的映射关系。
    /// </summary>
    public interface IReportStorage
    {
        /// <summary>
        /// 清除所有报告定义。
        /// </summary>
        void ClearAllReports();

        /// <summary>
        /// 删除指定 RPTID 的报告定义。
        /// </summary>
        /// <param name="rptId">报告ID</param>
        void RemoveReport(uint rptId);

        /// <summary>
        /// 添加或更新报告定义。
        /// </summary>
        /// <param name="rptId">报告ID</param>
        /// <param name="vidList">该报告包含的 VID 列表</param>
        void AddOrUpdateReport(uint rptId, List<uint> vidList);

        /// <summary>
        /// 检查指定 RPTID 是否已经定义。
        /// </summary>
        bool ContainsReport(uint rptId);
    }
}
