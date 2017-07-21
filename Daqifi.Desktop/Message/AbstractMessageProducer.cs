using System.IO;

namespace Daqifi.Desktop.Message
{
    public abstract class AbstractMessageProducer : IMessageProducer
    {
        #region Private Data

        #endregion

        #region Properties
        public Stream DataStream { get; protected set; }

        #endregion

        public abstract void SendAsync(IMessage message);
    }
}
