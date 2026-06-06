using Secs4Net;
using SECSbuilder;
using SECSdata;
using SECShandler.Interfaces;
using SECSparser;

namespace SECShandler.Functions
{
    /// <summary>
    /// 报警相关 SxFy 处理函数集合。
    /// </summary>
    public static class AlarmSxFyFunctions
    {
        /// <summary>
        /// 处理 S5F3（Enable/Disable Alarm Send）并回复 S5F4。
        /// ACKC5 约定：0=成功，1=拒绝，2=格式/内部错误。
        /// </summary>
        public static async Task HandleS5F3ReplyAsync(
            PrimaryMessageWrapper primary,
            IAlarmEnableStorage alarmEnableStorage)
        {
            byte ack = 0;
            S5F3_data request;

            try
            {
                request = S5F3_parser.Parse(primary.PrimaryMessage);
            }
            catch
            {
                ack = 2;
                await primary.TryReplyAsync(S5F4_builder.Build(ack)).ConfigureAwait(false);
                return;
            }

            try
            {
                var enable = request.ALED != 0;

                // if 关键分支：空 ALID 列表表示对全部报警使能状态生效。
                if (request.IsAllAlarms)
                {
                    alarmEnableStorage.SetAllAlarmsEnabled(enable);
                }
                else
                {
                    foreach (var alid in request.ALIDList)
                    {
                        alarmEnableStorage.SetAlarmEnabled(alid, enable);
                    }
                }
            }
            catch
            {
                ack = 2;
            }

            await primary.TryReplyAsync(S5F4_builder.Build(ack)).ConfigureAwait(false);
        }

        /// <summary>
        /// 处理 S5F5（List Alarm Request）并回复 S5F6。
        /// </summary>
        public static async Task HandleS5F5ReplyAsync(
            PrimaryMessageWrapper primary,
            IAlarmStateStorage alarmStateStorage)
        {
            S5F5_data request;
            try
            {
                request = S5F5_parser.Parse(primary.PrimaryMessage);
            }
            catch
            {
                // 关键分支：解析失败时返回空报警列表，避免 Host 等待超时。
                await primary.TryReplyAsync(S5F6_builder.Build(new S5F6_data())).ConfigureAwait(false);
                return;
            }

            var alarms = alarmStateStorage.GetActiveAlarms(request.IsAllAlarms ? null : request.ALIDList);
            var replyData = new S5F6_data
            {
                Alarms = alarms.ToList()
            };

            await primary.TryReplyAsync(S5F6_builder.Build(replyData)).ConfigureAwait(false);
        }
    }
}
