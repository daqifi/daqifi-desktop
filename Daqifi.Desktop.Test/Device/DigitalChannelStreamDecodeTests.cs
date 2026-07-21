using System.Linq;
using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Device;
using Daqifi.Core.Communication.Messages;
using Daqifi.Core.Device;
using Google.Protobuf;
using ChannelDirection = Daqifi.Core.Channel.ChannelDirection;
using ChannelType = Daqifi.Core.Channel.ChannelType;
using CoreStreamingDevice = Daqifi.Core.Device.DaqifiStreamingDevice;

namespace Daqifi.Desktop.Test.Device;

/// <summary>
/// Verifies the digital stream decode maps bits by channel number, not by position in the
/// active-channel list (issue #663). The firmware streams the whole DIO port as a raw
/// pin-state snapshot — bit N is pin N regardless of which channels are enabled — so
/// enabling a subset of digital channels must still read the correct pins.
/// Channel decoding itself is delegated to Core's <c>DaqifiStreamingDevice</c> (issue #613), so
/// these tests route frames through both the real desktop gating pipeline
/// (<see cref="DecodeTestDevice.RouteStreamFrame"/> calls <c>HandleInboundMessage</c>) and Core's
/// real decode step (via <see cref="TestCoreStreamingDevice.SimulateStreamFrame"/>), exactly
/// mirroring the synchronous order Core's actual message pump uses in production.
/// </summary>
[TestClass]
public class DigitalChannelStreamDecodeTests : IDisposable
{
    // Large enough to cover every digital channel index exercised below (up to DIO9).
    private const int DIGITAL_PORT_COUNT = 16;

    private DecodeTestDevice _device = null!;

    [TestInitialize]
    public void Setup()
    {
        _device = new DecodeTestDevice(DIGITAL_PORT_COUNT);
    }

    // MSTest disposes the test-class instance after each test, releasing the device's Core
    // connection instead of leaking one per test (CA1001).
    public void Dispose()
    {
        _device.Dispose();
        GC.SuppressFinalize(this);
    }

    private DigitalChannel ConfigureDigitalChannel(int index, ChannelDirection direction, bool isActive = true)
    {
        var channel = (DigitalChannel)_device.DataChannels.Single(
            c => c.Type == ChannelType.Digital && c.Index == index);
        channel.Direction = direction;
        channel.IsActive = isActive;
        return channel;
    }

    private void RouteDigitalData(params byte[] portSnapshot)
    {
        _device.RouteStreamFrame(new DaqifiOutMessage
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
        var dio4 = ConfigureDigitalChannel(4, ChannelDirection.Input);

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
        var dio4 = ConfigureDigitalChannel(4, ChannelDirection.Input);

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
        var dio1 = ConfigureDigitalChannel(1, ChannelDirection.Input);
        var dio3 = ConfigureDigitalChannel(3, ChannelDirection.Input);

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
        var dio9 = ConfigureDigitalChannel(9, ChannelDirection.Input);

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
        var dio9 = ConfigureDigitalChannel(9, ChannelDirection.Input);

        // Act
        RouteDigitalData(0b1111_1111);

        // Assert
        Assert.IsNull(dio9.ActiveSample, "A pin beyond the streamed payload must not get a sample");
    }

    [TestMethod]
    public void OutputDirectionChannel_GetsNoStreamedSample()
    {
        // Arrange — output channels display the commanded state, not streamed data
        var dio0 = ConfigureDigitalChannel(0, ChannelDirection.Output);

        // Act
        RouteDigitalData(0b0000_0001);

        // Assert
        Assert.IsNull(dio0.ActiveSample, "Output-direction channels must not be sampled from the stream");
    }

    [TestMethod]
    public void InactiveChannel_GetsNoSample()
    {
        // Arrange
        var dio2 = ConfigureDigitalChannel(2, ChannelDirection.Input, isActive: false);

        // Act — pin 2 high, but the channel is not enabled
        RouteDigitalData(0b0000_0100);

        // Assert
        Assert.IsNull(dio2.ActiveSample, "Inactive channels must not be sampled");
    }

    /// <summary>
    /// Test device exposing the real inbound-message pipeline (protocol handler → stream
    /// routing → Core decode → per-channel SampleReceived → desktop DataSample mapping).
    /// </summary>
    private sealed class DecodeTestDevice : AbstractStreamingDevice, IDisposable
    {
        private readonly TestCoreStreamingDevice _coreDevice;

        // The Core device connected in the constructor is owned by this fixture; disposing it
        // keeps the suite from leaking one connected device per test (CA1001).
        public void Dispose() => _coreDevice.Dispose();

        public DecodeTestDevice(int digitalPortCount)
        {
            _coreDevice = new TestCoreStreamingDevice();
            _coreDevice.Connect();

            var statusMessage = new DaqifiOutMessage
            {
                DevicePn = "Nq1",
                DeviceSn = 12345,
                DeviceFwRev = "1.0.0",
                DigitalPortNum = (uint)digitalPortCount
            };
            _coreDevice.Metadata.UpdateFromProtobuf(statusMessage);
            _coreDevice.PopulateChannelsFromStatus(statusMessage);

            // Wires the desktop DigitalChannel wrappers around Core's actual channel instances
            // (including the SampleReceived subscription installed by SyncChannelsFromCore).
            SyncFromCoreDevice(_coreDevice);

            InitializeDeviceState();

            _coreDevice.StreamingFrequency = 1;
            _coreDevice.StartStreaming();
            IsStreaming = true;
        }

        public override ConnectionType ConnectionType => ConnectionType.Usb;

        protected override CoreStreamingDevice? CoreDeviceForStreaming => _coreDevice;

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

        /// <summary>
        /// Routes a raw frame through both halves of the real production pipeline: the desktop's
        /// own gating/dispatch (<c>HandleInboundMessage</c>) and Core's decode step
        /// (<see cref="TestCoreStreamingDevice.SimulateStreamFrame"/>) — in that order, exactly as
        /// Core's actual <c>DaqifiStreamingDevice.OnStreamMessageReceived</c> sequences them in
        /// production (base call raises <c>MessageReceived</c> before Core decodes).
        /// </summary>
        public void RouteStreamFrame(DaqifiOutMessage message)
        {
            HandleInboundMessage(
                new MessageReceivedEventArgs(
                    new GenericInboundMessage<object>(message)));
            _coreDevice.SimulateStreamFrame(message);
        }
    }

    private sealed class TestCoreStreamingDevice() : CoreStreamingDevice("TestDevice")
    {
        public override void Send<T>(IOutboundMessage<T> message)
        {
        }

        public void SimulateStreamFrame(DaqifiOutMessage message) => OnStreamMessageReceived(message);
    }
}
