﻿namespace Daqifi.Desktop.IO.Messages.MessageTypes
{
    public class RawMessage : IMessage
    {
        public object Data { get; set; }

        public RawMessage(byte[] data)
        {
            Data = data;
        }

        public byte[] GetBytes()
        {
            return Data as byte[];
        }
    }
}
