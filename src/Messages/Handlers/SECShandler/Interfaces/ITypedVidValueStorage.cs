using System.Diagnostics.CodeAnalysis;
using Secs4Net;

namespace SECShandler.Interfaces
{
    /// <summary>
    /// 独立的 VID 类型化动态值缓存，不读写原 IVidValueStorage 字符串存储。
    /// </summary>
    public interface ITypedVidValueStorage
    {
        /// <summary>
        /// 按 VID 保存最新 Item 的深拷贝；调用方仍拥有传入值，可自行修改或释放。
        /// </summary>
        void UpsertByVid(ushort vid, Item value);

        /// <summary>
        /// 按 VID 获取独立深拷贝；命中时由调用方负责释放返回值，未命中时返回 null。
        /// </summary>
        bool TryGetByVid(ushort vid, [NotNullWhen(true)] out Item? value);
    }
}
