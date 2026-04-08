using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SECShandler.Interfaces
{
    public interface IEventLinkStorage
    {
        /// <summary>检查指定的 CEID 是否为设备支持的有效事件 ID。</summary>
        bool IsCeidValid(uint ceid);

        /// <summary>更新（替换）CEID 与 RPTID 列表的链接关系。</summary>
        void UpdateEventLinks(Dictionary<uint, List<uint>> links);

        /// <summary>获取某个 CEID 绑定的所有 RPTID。</summary>
        IReadOnlyList<uint> GetRptIdsForCeid(uint ceid);

        /// <summary>解除指定 CEID 的所有链接（相当于绑定空列表）。</summary>
        void UnlinkEvent(uint ceid);
    }
}
