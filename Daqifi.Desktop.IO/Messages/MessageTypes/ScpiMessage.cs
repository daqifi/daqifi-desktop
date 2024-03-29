﻿using System.Text;

namespace Daqifi.Desktop.IO.Messages.MessageTypes
{
    public class ScpiMessage : IMessage
    {
        public object Data { get; set; }

        public ScpiMessage(string command)
        {
            Data = command;
        }

        public byte[] GetBytes()
        {
            return Encoding.ASCII.GetBytes((string)Data + "\r\n");
        }
    }
}
