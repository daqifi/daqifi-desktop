using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Device;
using Daqifi.Core.Communication.Messages;
using Daqifi.Desktop.IO.Messages;
using Google.Protobuf;
using ChannelDirection = Daqifi.Core.Channel.ChannelDirection;
using CoreDigitalChannel = Daqifi.Core.Channel.DigitalChannel;

namespace Daqifi.Desktop.Test.Device;

/// <summary>
/// Verifies the digital stream decode maps bits by channel number, not by position in the
/// active-channel list (issue #663). The firmware streams the whole DIO port as a raw
/// pin-state snapshot — bit N is pin N regardless of which channels are enabled — so
/// enabling a subset of digital channels must still read the correct pins.
/// These tests route messages through the real inbound pipeline, not a copy of the logic.
/// </summary>
[TestClass]
public class DigitalChannelStreamDecodeTests
{
    private DecodeTestDevice _device;

    [TestInitialize]
    public void Setup()
    {
        _device = new DecodeTestDevice();
        _device.InitializeDeviceState();
        _device.IsStreaming = true;
    }

    private DigitalChannel AddDigitalChannel(int index, ChannelDirection direction, bool isActive = true)
    {
        var coreChannel = new CoreDigitalChannel(index)
        {
            Name = $"DIO{index}",
            Direction = direction
        };
        var channel = new DigitalChannel(_device, coreChannel)
        {
            IsActive = isActive
        };
        _device.DataChannels.Add(channel);
        return channel;
    }

    private void RouteDigitalData(params byte[] portSnapshot)
    {
        _device.RouteInboundMessage(new DaqifiOutMessage
        {
            MsgTimeStamp = 1000,
            DeviceSn = 12345,
            DeviceFwRev = "1.0.0",
            DigitalData = ByteString.CopyFrom(portSnapshot)
        });
    }

    [TestMethod]
    public void SubsetChannel_DIO4_ReadsItsOwnPinBit()
    {
        // Arrange — only DIO4 enabled. Positional mapping would read bit 0.
        var dio4 = AddDigitalChannel(4, ChannelDirection.Input);

        // Act — pin 4 high, pin 0 low
        RouteDigitalData(0b0001_0000);

        // Assert
        Assert.IsNotNull(dio4.ActiveSample, "DIO4 should receive a sample when it is the only active channel");
        Assert.AreEqual(1, dio4.ActiveSample.Value, "DIO4 should read its own pin bit (bit 4), not bit 0");
    }

    [TestMethod]
    public void SubsetChannel_DIO4_DoesNotReadPin0State()
    {
        // Arrange — only DIO4 enabled. The old positional decode read bit 0 here.
        var dio4 = AddDigitalChannel(4, ChannelDirection.Input);

        // Act — pin 0 high, pin 4 low
        RouteDigitalData(0b0000_0001);

        // Assert
        Assert.IsNotNull(dio4.ActiveSample);
        Assert.AreEqual(0, dio4.ActiveSample.Value, "DIO4 must not read pin 0's state");
    }

    [TestMethod]
    public void NonConsecutiveSubset_EachChannelReadsItsOwnPin()
    {
        // Arrange
        var dio1 = AddDigitalChannel(1, ChannelDirection.Input);
        var dio3 = AddDigitalChannel(3, ChannelDirection.Input);

        // Act — pin 1 low, pin 3 high
        RouteDigitalData(0b0000_1000);

        // Assert
        Assert.IsNotNull(dio1.ActiveSample);
        Assert.AreEqual(0, dio1.ActiveSample.Value, "DIO1 should read pin 1 (low)");
        Assert.IsNotNull(dio3.ActiveSample);
        Assert.AreEqual(1, dio3.ActiveSample.Value, "DIO3 should read pin 3 (high)");
    }

    [TestMethod]
    public void HighChannel_DIO9_ReadsSecondPayloadByte()
    {
        // Arrange — a channel above pin 7 alone; its bit lives in payload byte 1
        var dio9 = AddDigitalChannel(9, ChannelDirection.Input);

        // Act — pin 9 high (byte 1, bit 1)
        RouteDigitalData(0b0000_0000, 0b0000_0010);

        // Assert
        Assert.IsNotNull(dio9.ActiveSample, "DIO9 should receive a sample");
        Assert.AreEqual(1, dio9.ActiveSample.Value, "DIO9 should read byte 1 bit 1");
    }

    [TestMethod]
    public void ChannelBeyondPayload_GetsNoSample()
    {
        // Arrange — DIO9 needs payload byte 1, but the device only streamed one byte
        var dio9 = AddDigitalChannel(9, ChannelDirection.Input);

        // Act
        RouteDigitalData(0b1111_1111);

        // Assert
        Assert.IsNull(dio9.ActiveSample, "A pin beyond the streamed payload must not get a sample");
    }

    [TestMethod]
    public void OutputDirectionChannel_GetsNoStreamedSample()
    {
        // Arrange — output channels display the commanded state, not streamed data
        var dio0 = AddDigitalChannel(0, ChannelDirection.Output);

        // Act
        RouteDigitalData(0b0000_0001);

        // Assert
        Assert.IsNull(dio0.ActiveSample, "Output-direction channels must not be sampled from the stream");
    }

    [TestMethod]
    public void InactiveChannel_GetsNoSample()
    {
        // Arrange
        var dio2 = AddDigitalChannel(2, ChannelDirection.Input, isActive: false);

        // Act — pin 2 high, but the channel is not enabled
        RouteDigitalData(0b0000_0100);

        // Assert
        Assert.IsNull(dio2.ActiveSample, "Inactive channels must not be sampled");
    }

    /// <summary>
    /// Test device exposing the real inbound-message pipeline
    /// (protocol handler → stream routing → decode).
    /// </summary>
    private sealed class DecodeTestDevice : AbstractStreamingDevice
    {
        public override ConnectionType ConnectionType => ConnectionType.Usb;

        public override bool Connect() => true;

        public override bool Disconnect() => true;

        public override bool Write(string command) => true;

        protected override void SendMessage(IOutboundMessage<string> message)
        {
        }

        protected override void DispatchDeviceMessage(DeviceMessage deviceMessage)
        {
            // The logging pipeline needs the application service provider, which unit tests don't have.
        }

        public void RouteInboundMessage(DaqifiOutMessage message)
        {
            HandleInboundMessage(
                new MessageEventArgs<object>(
                    new GenericInboundMessage<object>(message)));
        }
    }
}
