using System.IO;
using System.Threading.Tasks;

namespace Daqifi.Desktop.Message.Producers
{
    public class MessageProducer : AbstractMessageProducer
    {
        #region Constructor
        public MessageProducer(Stream stream)
        {
            DataStream = stream;
        }
        #endregion

        #region AbstractMessageProducer overrides
        public override async Task SendAsync(IMessage message)
        {
            var serializedMessage = message.GetBytes();
            //await DataStream.WriteAsync(serializedMessage, 0, serializedMessage.Length);
            DataStream.Write(serializedMessage,0,serializedMessage.Length);
        }
        #endregion
    }
}
