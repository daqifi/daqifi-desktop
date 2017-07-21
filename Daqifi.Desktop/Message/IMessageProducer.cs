namespace Daqifi.Desktop.Message
{
    public interface IMessageProducer
    {
        void SendAsync(IMessage message);
    }
}
