using Secs4Net;
using SECSbuilder;
using SECSdata;
using SECShandler.Interfaces;
using SECSparser;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SECShandler.Handlers
{
    /// <summary>
    /// 处理 S2F41 (Remote Command) 消息的处理器。
    /// 主机通过此消息发送远程命令（如 START, STOP），设备需要执行对应操作并回复 S2F42。
    /// </summary>
    public class ControlDeviceHandler
    {
        private readonly SecsGem _secsGem;
        private readonly IDevice _device;

        /// <summary>
        /// 构造函数，通过依赖注入获取通信引擎和设备控制接口。
        /// </summary>
        /// <param name="secsGem">Secs4Net 消息引擎，用于发送回复。</param>
        /// <param name="device">设备业务接口，用于执行具体命令。</param>
        public ControlDeviceHandler(SecsGem secsGem, IDevice device)
        {
            _secsGem = secsGem;
            _device = device;
        }

        /// <summary>
        /// 处理 S2F41 消息。
        /// </summary>
        /// <param name="primary">收到的原始 S2F41 消息。</param>
        public async Task HandleS2F41ReplyAsync(PrimaryMessageWrapper primary)
        {
            S2F41_data data;
            byte drack = 0; // 默认成功
            var primaryMsg = primary.PrimaryMessage;
            try
            {
                // 1. 解析消息
                data = S2F41_parser.Parse(primaryMsg);
            }
            catch (Exception ex)
            {
                // 解析失败：格式错误，回复 HCACK = 2 (无法执行)
                Console.WriteLine($"S2F41 parse error: {ex.Message}");
                // 回复时使用原Message，避免SystemBytes不匹配导致主机无法识别回复
                await primary.TryReplyAsync(S2F42_builder.Build(drack));
                return;
            }

            try
            {
                // 2. 根据命令名执行不同的业务逻辑,需注意！！！Host端发送的RCMD的长度需匹配，例如START是：A 5 RCMD 'START';STOP是：A 4 RCMD 'STOP'，否则会因多空格解析失败
                switch (data.RCMD)
                {
                    case "START":
                        // 从参数中获取 LOTID（批次号）
                        string? lotId = null;
                        if (data.Parameters.TryGetValue("LOTID", out var lotIdItem))
                        {
                            // 参数值可能是 Item 类型，提取字符串
                            if (lotIdItem is Item item && item.Format == SecsFormat.ASCII)
                                lotId = item.GetString();
                        }
                        await _device.StartProcessAsync(lotId);
                        break;

                    case "STOP":
                        await _device.StopProcessAsync();
                        break;

                    case "PAUSE":
                        await _device.PauseProcessAsync();
                        break;

                    case "RESUME":
                        await _device.ResumeProcessAsync();
                        break;

                    default:
                        drack = 1; // 命令不存在
                        Console.WriteLine($"Error RCMD storage error: {data.RCMD}");
                        await primary.TryReplyAsync(S2F42_builder.Build(drack));
                        return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"S2F33 storage error: {ex.Message}");
                drack = 1; // 其他错误，如内存不足
                await primary.TryReplyAsync(S2F42_builder.Build(drack));
                return;
            }

            // 3. 发送成功回复 S2F42
            await primary.TryReplyAsync(S2F42_builder.Build(drack));
        }
    }
}
