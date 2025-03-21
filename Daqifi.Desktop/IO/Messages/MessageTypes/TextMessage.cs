using System.Text;

namespace Daqifi.Desktop.IO.Messages.MessageTypes;

public class TextMessage : IMessage
{
    public object Data { get; set; }

    public TextMessage(string text)
    {
        Data = text;
    }

    public byte[] GetBytes()
    {
        return Encoding.ASCII.GetBytes(Data as string ?? string.Empty);
    }
}