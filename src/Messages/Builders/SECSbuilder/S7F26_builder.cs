using Secs4Net;
using SECSdata;

namespace SECSbuilder
{
    /// <summary>
    /// S7F26 格式化配方响应构建器。
    /// </summary>
    public static class S7F26_builder
    {
        /// <summary>
        /// 将格式化配方构建为 PPID、MDLN、SOFTREV 和命令列表结构。
        /// </summary>
        public static SecsMessage Build(S7F26_data data)
        {
            ArgumentNullException.ThrowIfNull(data);
            var commandItems = new List<Item>();
            foreach (var command in data.commands ?? [])
            {
                // 每条命令编码为命令码及 ASCII 参数列表。
                Item[] parameterItems = (command.parameters ?? [])
                    .Select(parameter => Item.A(parameter ?? string.Empty))
                    .ToArray();
                commandItems.Add(Item.L(
                    Item.U2(command.commandCode),
                    Item.L(parameterItems)));
            }

            return new SecsMessage(7, 26, replyExpected: false)
            {
                Name = "FormattedProcessProgramData",
                SecsItem = Item.L(
                    Item.A(data.recipeName ?? string.Empty),
                    Item.A(data.modelName ?? string.Empty),
                    Item.A(data.softwareRevision ?? string.Empty),
                    Item.L(commandItems.ToArray()))
            };
        }
    }
}
