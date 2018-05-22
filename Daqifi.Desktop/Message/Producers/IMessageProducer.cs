namespace Daqifi.Desktop.Message.Producers
{
    public interface IMessageProducer
    {
        void Send(IMessage message);
    }
}
