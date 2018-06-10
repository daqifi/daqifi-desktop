using Daqifi.Desktop.IO.Messages.MessageTypes;

namespace Daqifi.Desktop.IO.Messages.Producers
{
    public interface IMessageProducer
    {
        void Send(IMessage message);
    }
}
