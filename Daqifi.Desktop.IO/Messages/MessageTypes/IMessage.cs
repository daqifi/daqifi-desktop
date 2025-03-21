
namespace Daqifi.Desktop.IO.Messages.MessageTypes;

public interface IMessage
{
    object Data { get; set;}

    byte[] GetBytes();
}