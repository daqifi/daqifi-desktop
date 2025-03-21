using System.Text;

namespace Daqifi.Desktop.IO.Messages.MessageTypes;

public class TextMessage(string text) : IMessage
{
    public object Data { get; set; } = text;

    public byte[] GetBytes()
    {
        return Encoding.ASCII.GetBytes(Data as string ?? string.Empty);
    }
}