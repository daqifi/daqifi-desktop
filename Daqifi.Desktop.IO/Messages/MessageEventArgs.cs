﻿using Daqifi.Desktop.IO.Messages.MessageTypes;

namespace Daqifi.Desktop.IO.Messages;

//TODO make all of this a generic message not just protobuf message
public class MessageEventArgs : EventArgs
{
    #region Properties
    public IMessage Message { get; }

    #endregion

    public MessageEventArgs(IMessage message)
    {
        Message = message;
    }
}