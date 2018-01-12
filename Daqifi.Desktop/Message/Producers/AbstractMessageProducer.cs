using System.IO;
using System.Threading.Tasks;

namespace Daqifi.Desktop.Message.Producers
{
    public abstract class AbstractMessageProducer : IMessageProducer
    {
        #region Private Data

        #endregion

        #region Properties
        public Stream DataStream { get; protected set; }

        #endregion

        public abstract Task SendAsync(IMessage message);
    }
}
