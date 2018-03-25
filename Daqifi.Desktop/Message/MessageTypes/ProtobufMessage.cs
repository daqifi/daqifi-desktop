namespace Daqifi.Desktop.Message.MessageTypes
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
