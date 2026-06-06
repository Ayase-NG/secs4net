using Secs4Net;
using SECSbuilder;
using SECShandler.Interfaces;
using SECSparser;

namespace SECShandler.Functions
{
    /// <summary>
    /// 时间同步相关 SxFy 处理函数。
    /// </summary>
    public static class TimeSyncSxFyFunctions
    {
        /// <summary>
        /// 处理 S2F31 并返回 S2F32。
        /// </summary>
        public static async Task HandleS2F31ReplyAsync(
            PrimaryMessageWrapper primary,
            ITimeSyncStorage timeSyncStorage)
        {
            byte tiack = 0;

            try
            {
                var request = S2F31_parser.Parse(primary.PrimaryMessage);
                timeSyncStorage.UpdateHostTime(request.HostTimeUtc, request.TimeText);
            }
            catch
            {
                // if 关键分支：解析或存储失败时返回 TIACK=2。
                tiack = 2;
            }

            await primary.TryReplyAsync(S2F32_builder.Build(tiack)).ConfigureAwait(false);
        }
    }
}
