using System.IO;

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
        public override void SendAsync(IMessage message)
        {
            var serializedMessage = message.GetBytes();
            DataStream.WriteAsync(serializedMessage, 0, serializedMessage.Length);
        }
        #endregion
    }
}
