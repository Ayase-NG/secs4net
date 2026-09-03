using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging;
using Secs4Net;
using SECSbuilder;
using SECSdata;
using SECShandler.Interfaces;
using SECSparser;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SECShandler.Functions
{
    /// <summary>
    /// 配方相关 SxFy 处理函数。
    /// </summary>
    public static class RecipeSxFyFunctions
    {
        /// <summary>
        /// 处理 S7F5 配方请求，并在处理失败时记录错误及返回对应的错误码。
        /// </summary>
        public static async Task HandleS7F5ReplyAsync(SecsGem secsGem, PrimaryMessageWrapper primary, ILogger logger)
        {
            // 处理 S7F5 回复消息
            var replyMessage = primary.PrimaryMessage;
            if (replyMessage == null)
            {
                // 记录缺少 S7F5 回复消息的异常，以便排查通信状态。
                var exception = new InvalidOperationException("S7F5 reply message is null.");
                logger.LogError(exception, "Unable to process S7F5 because the reply message is null.");
                throw exception;
            }
            try
            {
                // 解析回复消息中的数据,加载对应的配方文件
                S7F5_data items = S7F5_parser.Parse(primary.PrimaryMessage);
                string basePath = "D:\\GY-Recipes\\Recipes";
                string recipePath = Path.Combine(basePath, items.recipeName + ".json");
                string content = File.ReadAllText(recipePath, Encoding.ASCII);
                byte[] file = Encoding.ASCII.GetBytes(content);
                var s7f6 = S7F6_builder.Build(new S7F6_data
                {
                    recipeName = items.recipeName,
                    recipeContent = file
                });
                var reply = await primary.TryReplyAsync(s7f6).ConfigureAwait(false);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                // 记录 PPID 格式错误，便于追踪 Host 发送的无效配方请求。
                logger.LogError(ex, "Failed to process S7F5 because the PPID format is invalid.");
                var reply = new SecsMessage(7, 6, false)
                {
                    SecsItem = Item.L(
                        Item.U4(new uint[] { 1 }), // Error code:PPID不为ASCII，格式错误
                        Item.B()
                    )
                };
                // 异步发送 PPID 格式错误的 S7F6 回复给 Host。
                await secsGem.SendAsync(reply).ConfigureAwait(false);
            }
            catch (ArgumentNullException ex)
            {
                // 记录缺少 PPID 的错误，便于追踪无效的配方请求。
                logger.LogError(ex, "Failed to process S7F5 because the PPID is empty.");
                var reply = new SecsMessage(7, 6, false)
                {
                    SecsItem = Item.L(
                        Item.U4(new uint[] { 2 }), // Error code:PPID为空
                        Item.B()
                    )
                };
                // 异步发送 PPID 为空的 S7F6 回复给 Host。
                await secsGem.SendAsync(reply).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 记录未预期的处理失败，保留异常堆栈用于故障诊断。
                logger.LogError(ex, "An unexpected error occurred while processing S7F5.");

                var reply = new SecsMessage(7, 6, false)
                {
                    SecsItem = Item.L(
                        Item.U4(new uint[] { 3 }), // Error code:内部错误
                        Item.B()
                    )
                };
                // 异步发送内部错误的 S7F6 回复给 Host。
                await secsGem.SendAsync(reply).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 处理 S7F19 配方目录请求，并通过 S7F20 返回当前可用的配方名称。
        /// </summary>
        public static async Task HandleS7F19ReplyAsync(SecsGem secsGem, PrimaryMessageWrapper primary, ILogger logger)
        {
            try
            {
                // 校验 S7F19 消息结构，S7F19 标准消息不包含数据项。
                S7F19_parser.Parse(primary.PrimaryMessage);
                string basePath = "D:\\GY-Recipes\\Recipes";
                // 返回配方根目录下的一级文件夹名称，并保持稳定的名称顺序。
                var recipeNames = Directory.EnumerateDirectories(basePath, "*", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Cast<string>()
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var s7f20 = S7F20_builder.Build(new S7F20_data
                {
                    recipeNames = recipeNames
                });
                // 异步回复 S7F20 配方目录，并关联原 S7F19 事务。
                await primary.TryReplyAsync(s7f20).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 记录目录读取或消息构建异常，并返回空目录避免 Host 持续等待。
                logger.LogError(ex, "An unexpected error occurred while processing S7F19.");
                var errorReply = S7F20_builder.Build(new S7F20_data());
                // 异步回复空的 S7F20 配方目录，并关联原 S7F19 事务。
                await primary.TryReplyAsync(errorReply).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 处理 S7F25 格式化配方请求，并通过 S7F26 返回配方内容。
        /// </summary>
        public static async Task HandleS7F25ReplyAsync(SecsGem secsGem, PrimaryMessageWrapper primary, ILogger logger)
        {
            try
            {
                // 解析并校验 Host 请求的格式化配方名称。
                S7F25_data request = S7F25_parser.Parse(primary.PrimaryMessage);
                string basePath = "D:\\GY\\Recipes";
                string safeRecipeName = Path.GetFileName(request.recipeName);
                if (!string.Equals(request.recipeName, safeRecipeName, StringComparison.Ordinal))
                {
                    // 拒绝包含路径片段的 PPID，防止读取配方目录之外的文件。
                    throw new ArgumentException("S7F25 PPID contains an invalid path.", nameof(primary));
                }

                string recipePath = Path.Combine(basePath, safeRecipeName + ".json");
                // 异步读取格式化配方文件，避免阻塞消息处理线程。
                string content = await File.ReadAllTextAsync(recipePath, Encoding.UTF8).ConfigureAwait(false);
                // 将Recipe JSON内容反序列化为 S7F26_data 对象，确保属性名称不区分大小写。
                var recipe = JsonSerializer.Deserialize<S7F26_data>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? throw new InvalidDataException($"Recipe '{safeRecipeName}' is empty or invalid.");
                recipe.recipeName = string.IsNullOrWhiteSpace(recipe.recipeName) ? request.recipeName : recipe.recipeName;

                var s7f26 = S7F26_builder.Build(recipe);
                // 异步回复 S7F26 格式化配方，并关联原 S7F25 事务。
                await primary.TryReplyAsync(s7f26).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 记录配方读取、反序列化或消息构建异常，并返回空格式化配方。
                logger.LogError(ex, "An unexpected error occurred while processing S7F25.");
                var errorReply = S7F26_builder.Build(new S7F26_data());
                // 异步回复空的 S7F26，确保原 S7F25 事务正常结束。
                await primary.TryReplyAsync(errorReply).ConfigureAwait(false);
            }
        }
    }
}
