using Daqifi.Desktop.Message;

namespace DAQifi.Desktop.Message
{
    public class RawMessage : IMessage
    {
        public object Data { get; set; }

        public RawMessage(byte[] data)
        {
            Data = data;
        }

        public byte[] GetBytes()
        {
            return Data as byte[];
        }
    }
}
