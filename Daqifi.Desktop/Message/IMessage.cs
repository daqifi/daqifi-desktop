using System;

namespace Daqifi.Desktop.Message
{
    public interface IMessage
    {
        object Data { get; set;}

        byte[] GetBytes();
    }
}
