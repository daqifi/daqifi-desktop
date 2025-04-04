using Daqifi.Core.Communication.Messages;

namespace Daqifi.Desktop.IO.Messages.Producers;

public interface IMessageProducer
{
    void Start();
    void Stop();
    void StopSafely();
    void Send(IMessage message);
}