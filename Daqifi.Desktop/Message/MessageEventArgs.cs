using System;

namespace Daqifi.Desktop.Message
{
    //TODO make all of this a generic message not just protobuf message
    public class MessageEventArgs : EventArgs
    {
        #region Private Data

        #endregion

        #region Properties
        public IMessage Message { get; }

        #endregion

        public MessageEventArgs(IMessage message)
        {
            Message = message;
        }
    }
}
