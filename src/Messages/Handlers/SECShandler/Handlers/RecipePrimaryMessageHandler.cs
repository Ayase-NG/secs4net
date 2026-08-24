using Microsoft.Extensions.Logging;
using Secs4Net;
using SECShandler.Functions;
using SECShandler.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SECShandler.Handlers
{
    public sealed class RecipePrimaryMessageHandler : IPrimaryMessageHandler
    {
        private readonly ILogger<RecipePrimaryMessageHandler> logger;

        /// <summary>
        /// 初始化配方主消息处理器及其日志记录器。
        /// </summary>
        public RecipePrimaryMessageHandler(ILogger<RecipePrimaryMessageHandler> logger)
        {
            this.logger = logger;
        }
        public IEnumerable<(int S, int F)> SupportedMessages => 
            [
                (7,5),
                (7,19),
                (7,25)
            ];

        public bool CanHandle(int s, int f) => (s, f) is (7, 5) or (7, 19) or (7, 25);

        /// <summary>
        /// 根据主消息的 Stream 和 Function 分派配方相关处理逻辑。
        /// </summary>
        public async Task HandleAsync(SecsGem secsGem, PrimaryMessageWrapper primaryMessage, CancellationToken cancellationToken)
        {
            var msg = primaryMessage.PrimaryMessage;
            switch ((msg.S, msg.F))
            {   
                case (7, 5):
                    // Handle S7F5 message
                    Console.WriteLine("Handling S7F5 message");
                    // 异步处理 S7F5 请求，并传递日志记录器以记录处理错误。
                    await RecipeSxFyFunctions.HandleS7F5ReplyAsync(secsGem, primaryMessage, logger);
                    break;
                case (7, 19):
                    // Handle S7F19 message
                    Console.WriteLine("Handling S7F19 message");
                    // 异步处理 S7F19 请求并返回设备中的配方目录。
                    await RecipeSxFyFunctions.HandleS7F19ReplyAsync(secsGem, primaryMessage, logger);
                    break;
                case (7, 25):
                    // Handle S7F25 message
                    Console.WriteLine("Handling S7F25 message");
                    // 异步处理 S7F25 请求并返回指定的格式化配方。
                    await RecipeSxFyFunctions.HandleS7F25ReplyAsync(secsGem, primaryMessage, logger);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported message type: S{msg.S}F{msg.F}");
            }
        }
    }
}
