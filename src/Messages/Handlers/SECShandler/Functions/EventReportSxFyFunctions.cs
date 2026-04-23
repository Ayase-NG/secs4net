using Secs4Net;
using SECShandler.Interfaces;
using SECSbuilder;
using SECSdata;
using SECSparser;

namespace SECShandler.Functions
{
    public static class EventReportSxFyFunctions
    {
        public static async Task HandleS2F33ReplyAsync(
            PrimaryMessageWrapper primary,
            IReportStorage reportStorage)
        {
            S2F33_data data;
            byte drack = 0;
            var primaryMsg = primary.PrimaryMessage;
            try
            {
                data = S2F33_parser.Parse(primaryMsg);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"S2F33 parse error: {ex.Message}");
                drack = 2;
                await primary.TryReplyAsync(S2F34_builder.Build(drack));
                return;
            }

            try
            {
                if (data.IsDeleteAll)
                {
                    reportStorage.ClearAllReports();
                }
                else
                {
                    foreach (var rptId in data.DeletedRptIds)
                    {
                        reportStorage.RemoveReport(rptId);
                    }

                    foreach (var kvp in data.DefinedReports)
                    {
                        reportStorage.AddOrUpdateReport(kvp.Key, kvp.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"S2F33 storage error: {ex.Message}");
                drack = 1;
                await primary.TryReplyAsync(S2F34_builder.Build(drack));
                return;
            }

            await primary.TryReplyAsync(S2F34_builder.Build(drack));
        }

        public static async Task HandleS2F35ReplyAsync(
            PrimaryMessageWrapper primary,
            IReportStorage reportStorage,
            IEventLinkStorage eventLinkStorage)
        {
            S2F35_data data;
            byte lrack = 0;
            var primaryMsg = primary.PrimaryMessage;
            try
            {
                data = S2F35_parser.Parse(primaryMsg);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"S2F35 parse error: {ex.Message}");
                lrack = 2;
                await primary.TryReplyAsync(S2F36_builder.Build(lrack));
                return;
            }

            try
            {
                foreach (var kvp in data.Links)
                {
                    if (!eventLinkStorage.IsCeidValid(kvp.Key))
                    {
                        lrack = 4;
                        break;
                    }
                    foreach (var rptId in kvp.Value)
                    {
                        if (!reportStorage.ContainsReport(rptId))
                        {
                            lrack = 5;
                            break;
                        }
                    }
                    if (lrack != 0) break;
                }

                if (lrack == 0)
                {
                    eventLinkStorage.UpdateEventLinks(data.Links);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"S2F35 storage error: {ex.Message}");
                lrack = 1;
                await primary.TryReplyAsync(S2F36_builder.Build(lrack));
                return;
            }

            await primary.TryReplyAsync(S2F36_builder.Build(lrack));
        }

        public static async Task HandleS2F37ReplyAsync(
            PrimaryMessageWrapper primary,
            IEventLinkStorage eventLinkStorage,
            IEventEnableStorage eventEnableStorage)
        {
            var primaryMsg = primary.PrimaryMessage;
            S2F37_data data;
            byte eac = 0;

            try
            {
                data = S2F37_parser.Parse(primaryMsg);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"S2F37 parse error: {ex.Message}");
                eac = 2;
                await primary.TryReplyAsync(S2F38_builder.Build(eac));
                return;
            }

            try
            {
                if (data.IsAllEvents)
                {
                    eventEnableStorage.EnableAllEvents();
                }
                else
                {
                    foreach (var ceid in data.CeidList)
                    {
                        if (!eventLinkStorage.IsCeidValid(ceid))
                        {
                            eac = 1;
                            break;
                        }
                        eventEnableStorage.EnableEvent(ceid);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"S2F37 storage error: {ex.Message}");
                eac = 2;
                await primary.TryReplyAsync(S2F38_builder.Build(eac));
                return;
            }

            await primary.TryReplyAsync(S2F38_builder.Build(eac));
        }
    }
}
