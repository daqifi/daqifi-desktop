using System.IO;

namespace Daqifi.Desktop.Message.Producers
{
    public class MessageProducer : IMessageProducer
    {
        public Stream DataStream { get; protected set; }

        #region Constructor
        public MessageProducer(Stream stream)
        {
            DataStream = stream;
        }
        #endregion

        #region AbstractMessageProducer overrides
        public void Send(IMessage message)
        {
            var serializedMessage = message.GetBytes();
            //await DataStream.WriteAsync(serializedMessage, 0, serializedMessage.Length);
            DataStream.Write(serializedMessage,0,serializedMessage.Length);
        }
        #endregion
    }
}
