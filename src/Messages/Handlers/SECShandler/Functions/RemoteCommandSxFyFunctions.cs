using Secs4Net;
using SECSbuilder;
using SECSdata;
using SECShandler.Interfaces;
using SECSparser;

namespace SECShandler.Functions
{
    /// <summary>
    /// 远程命令相关的 SxFy 处理函数集合。
    /// 当前包含 S2F41（Remote Command）解析与 S2F42 应答逻辑。
    /// </summary>
    public static class RemoteCommandSxFyFunctions
    {
        /// <summary>
        /// 处理 S2F41 Remote Command。
        /// 支持 RCMD：START、STOP、PAUSE、RESUME、PPSELECT。
        /// </summary>
        public static async Task HandleS2F41Async(
            PrimaryMessageWrapper primary,
            IDevice device,
            IMeasurementDispatcher measurementDispatcher,
            ISecsInteractionHistoryStore interactionHistoryStore,
            CancellationToken cancellationToken)
        {
            S2F41_data data;
            var primaryMsg = primary.PrimaryMessage;
            try
            {
                data = S2F41_parser.Parse(primaryMsg);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"S2F41 parse error: {ex.Message}");
                await TryReplyS2F42Async(primary, hcack: 2, interactionHistoryStore, cancellationToken, rcmd: null, errorParam: "Parse");
                return;
            }

            var rcmd = (data.RCMD ?? string.Empty).Trim().ToUpperInvariant();

            // if 关键分支：只有 On-Line Remote 状态才允许远程命令执行。
            if (device.IsOnline is not DeviceOnlineState.OnLineRemote)
            {
                await TryReplyS2F42Async(primary, hcack: 2, interactionHistoryStore, cancellationToken, rcmd, errorParam: "NotOnlineState");
                return;
            }

            try
            {
                switch (rcmd)
                {
                    case "START":
                        await measurementDispatcher.DispatchStartMeasurementAsync(data, cancellationToken);
                        break;
                    case "STOP":
                        await measurementDispatcher.DispatchStopMeasurementAsync(data, cancellationToken);
                        break;
                    case "PAUSE":
                        await measurementDispatcher.DispatchPauseMeasurementAsync(data, cancellationToken);
                        break;
                    case "RESUME":
                        await measurementDispatcher.DispatchResumeMeasurementAsync(data, cancellationToken);
                        break;
                    case "PPSELECT":
                        await measurementDispatcher.DispatchProcessProgramSelectAsync(data, cancellationToken);
                        break;
                    default:
                        await TryReplyS2F42Async(primary, hcack: 1, interactionHistoryStore, cancellationToken, rcmd, errorParam: "RCMD");
                        return;
                }

                await TryReplyS2F42Async(primary, hcack: 0, interactionHistoryStore, cancellationToken, rcmd);
            }
            catch
            {
                await TryReplyS2F42Async(primary, hcack: 2, interactionHistoryStore, cancellationToken, rcmd);
                throw;
            }
        }

        /// <summary>
        /// 回复 S2F42（Remote Command Acknowledge）并记录数据库追溯日志。
        /// HCACK 约定：0=成功，1=命令不支持/参数问题，2=执行失败。
        /// </summary>
        private static async Task TryReplyS2F42Async(
            PrimaryMessageWrapper primaryMessage,
            byte hcack,
            ISecsInteractionHistoryStore interactionHistoryStore,
            CancellationToken cancellationToken,
            string? rcmd = null,
            string? errorParam = null)
        {
            if (!primaryMessage.PrimaryMessage.ReplyExpected)
                return;

            var paramAcks = string.IsNullOrWhiteSpace(errorParam)
                ? Item.L()
                : Item.L(Item.L(Item.A(errorParam), Item.B((byte)1)));

            var s2f42 = new SecsMessage(2, 42, replyExpected: false)
            {
                Name = "RemoteCommandAcknowledge",
                SecsItem = Item.L(Item.B(hcack), paramAcks)
            };

            await primaryMessage.TryReplyAsync(s2f42, cancellationToken);

            var sxFy = $"S{s2f42.S}F{s2f42.F}";
            var secsMessage = s2f42.ToString();
            await interactionHistoryStore.SaveInteractionAsync(sxFy, secsMessage, hcack, DateTime.UtcNow, cancellationToken);
        }
    }
}
