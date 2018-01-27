﻿using System.Text;

namespace Daqifi.Desktop.Message.MessageTypes
{
    public class ScpiMessage : AbstractMessage
    {
        public ScpiMessage(string command)
        {
            Data = command;
        }

        public override byte[] GetBytes()
        {
            return Encoding.ASCII.GetBytes((string)Data + "\r\n");
        }
    }
}