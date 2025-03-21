using System.Text;

namespace Daqifi.Desktop.IO.Messages.MessageTypes;

public class ScpiMessage(string command) : IMessage
{
    public object Data { get; set; } = command;

    public byte[] GetBytes()
    {
        return Encoding.ASCII.GetBytes((string)Data + "\r\n");
    }
}