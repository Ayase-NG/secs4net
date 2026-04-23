using Secs4Net;
using SECShandler.Interfaces;

namespace SECShandler.Functions
{
    public static class CommunicationSxFyFunctions
    {
        public static async Task HandleS1F1ReplyAsync(SecsGem secsGem, IDevice device, PrimaryMessageWrapper primary)
        {
            var reply = new SecsMessage(1, 2, replyExpected: false)
            {
                Name = "OnlineData",
                SecsItem = Item.L(
                    Item.A(device.ModelNumber),
                    Item.A(device.SoftwareRevision)
                )
            };

            try
            {
                await primary.TryReplyAsync(reply).ConfigureAwait(false);
            }
            catch (Exception)
            {
                await secsGem.SendAsync(reply).ConfigureAwait(false);
            }
        }

        public static async Task HandleS1F13ReplyAsync(SecsGem secsGem, IDevice device, PrimaryMessageWrapper primary)
        {
            var accept = device.IsOnline;

            var reply = new SecsMessage(1, 14, replyExpected: false)
            {
                Name = "EstablishCommunicationsAcknowledge",
                SecsItem = Item.L(
                    Item.B(new byte[] { accept ? (byte)0 : (byte)1 }),
                    Item.L(
                        Item.A(device.ModelNumber),
                        Item.A(device.SoftwareRevision)
                    )
                )
            };

            try
            {
                await primary.TryReplyAsync(reply).ConfigureAwait(false);
            }
            catch (Exception)
            {
                await secsGem.SendAsync(reply).ConfigureAwait(false);
            }
        }
    }
}
