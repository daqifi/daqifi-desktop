using Daqifi.Core.Communication.Messages;

namespace Daqifi.Desktop.IO.Messages;

public class MessageEventArgs<T> : EventArgs
{
    #region Properties
    public IInboundMessage<T> Message { get; }

    #endregion

    public MessageEventArgs(IInboundMessage<T> message)
    {
        Message = message;
    }
}