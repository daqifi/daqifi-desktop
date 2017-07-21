namespace Daqifi.Desktop.Message
{
    public class ProtobufMessage : AbstractMessage
    {
        public ProtobufMessage(WiFiDAQOutMessage message)
        {
            Data = message;
        }

        public override byte[] GetBytes()
        {
            return ((WiFiDAQOutMessage)Data).ToByteArray();
        }
    }
}
