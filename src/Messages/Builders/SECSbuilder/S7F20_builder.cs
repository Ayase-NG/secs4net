using Secs4Net;
using SECSdata;

namespace SECSbuilder
{
    /// <summary>
    /// S7F20 配方目录响应构建器。
    /// </summary>
    public static class S7F20_builder
    {
        /// <summary>
        /// 将配方名称列表构建为 S7F20 ASCII Item 列表。
        /// </summary>
        public static SecsMessage Build(S7F20_data data)
        {
            ArgumentNullException.ThrowIfNull(data);
            // 每个 PPID 均编码为 S7F20 根列表中的 ASCII Item。
            Item[] recipeItems = (data.recipeNames ?? [])
                .Select(name => Item.A(name ?? string.Empty))
                .ToArray();

            return new SecsMessage(7, 20, replyExpected: false)
            {
                Name = "CurrentEPPD",
                SecsItem = Item.L(recipeItems)
            };
        }
    }
}
