namespace Daqifi.Desktop.Message.MessageTypes
{
    public class ProtobufMessage : AbstractMessage
    {
        public ProtobufMessage(DaqifiOutMessage message)
        {
            Data = message;
        }

        public override byte[] GetBytes()
        {
            return ((DaqifiOutMessage)Data).ToByteArray();
        }
    }
}
