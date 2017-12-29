namespace Daqifi.Desktop.Message.Producers
{
    public interface IMessageProducer
    {
        void SendAsync(IMessage message);
    }
}
