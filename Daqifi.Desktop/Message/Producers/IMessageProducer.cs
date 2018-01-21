using System.Threading.Tasks;

namespace Daqifi.Desktop.Message.Producers
{
    public interface IMessageProducer
    {
        Task SendAsync(IMessage message);
    }
}
