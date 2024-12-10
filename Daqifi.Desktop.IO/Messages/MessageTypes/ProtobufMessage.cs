using Google.Protobuf;
namespace Daqifi.Desktop.IO.Messages.MessageTypes
{
    public class ProtobufMessage : IMessage
    {
        public object Data { get; set; }

        public ProtobufMessage(DaqifiOutMessage message)
        {
            Data = message;
        }

        public byte[] GetBytes()
        {
            return ((DaqifiOutMessage)Data).ToByteArray();
        }
    }
}
