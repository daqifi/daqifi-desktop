using Daqifi.Core.Communication.Messages;

namespace Daqifi.Desktop.IO.Messages;

/// <summary>
/// Represents a message containing binary data from the DAQiFi device.
/// Used for SD card file downloads and other binary data transfers.
/// </summary>
public class BinaryMessage : IInboundMessage<byte[]>
{
    /// <summary>
    /// Gets the binary data associated with the message.
    /// </summary>
    public byte[] Data { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="BinaryMessage"/> class.
    /// </summary>
    /// <param name="data">The binary data received from the device.</param>
    public BinaryMessage(byte[] data)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
    }
}
