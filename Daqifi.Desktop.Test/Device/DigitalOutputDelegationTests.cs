using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Device;
using Daqifi.Core.Communication.Messages;
using ChannelDirection = Daqifi.Core.Channel.ChannelDirection;
using CoreStreamingDevice = Daqifi.Core.Device.DaqifiStreamingDevice;

namespace Daqifi.Desktop.Test.Device;

/// <summary>
/// Verifies that digital direction and output-state commands delegate to Core's
/// SetDioDirection/SetDioValue (issue #663) instead of hand-building SCPI in desktop,
/// and that the pre-delegation no-op-when-disconnected semantics are preserved
/// (Core throws when disconnected; the desktop wrapper must not, see issue #619).
/// </summary>
[TestClass]
public class DigitalOutputDelegationTests
{
    private DioTestDevice _device;
    private RecordingCoreDevice _coreDevice;

    [TestInitialize]
    public void Setup()
    {
        _device = new DioTestDevice();
        _coreDevice = new RecordingCoreDevice();
        _coreDevice.PopulateChannelsFromStatus(new DaqifiOutMessage
        {
            DevicePn = "Nq1",
            DeviceSn = 12345,
            DeviceFwRev = "1.0.0",
            DigitalPortNum = 16
        });
        _coreDevice.Connect();
        _device.SetCoreDevice(_coreDevice);
    }

    private DigitalChannel WrapCoreChannel(int index)
    {
        var coreChannel = _coreDevice.Channels
            .OfType<Daqifi.Core.Channel.IDigitalChannel>()
            .First(c => c.ChannelNumber == index);
        var channel = new DigitalChannel(_device, coreChannel);
        _device.DataChannels.Add(channel);
        return channel;
    }

    [TestMethod]
    public void SetChannelOutputValue_High_DelegatesToCoreAndMirrorsOutputValue()
    {
        // Arrange
        var channel = WrapCoreChannel(3);
        channel.Direction = ChannelDirection.Output;
        _coreDevice.SentCommands.Clear();

        // Act
        _device.SetChannelOutputValue(channel, 1);

        // Assert — Core mirrors the commanded state into the core channel and sends the command
        Assert.IsTrue(channel.CoreChannel.OutputValue, "Core should mirror the commanded HIGH state");
        Assert.AreEqual(1, _coreDevice.SentCommands.Count, "Exactly one device command should be sent");
    }

    [TestMethod]
    public void SetChannelOutputValue_Low_DelegatesToCoreAndMirrorsOutputValue()
    {
        // Arrange
        var channel = WrapCoreChannel(3);
        channel.Direction = ChannelDirection.Output;
        _device.SetChannelOutputValue(channel, 1);
        _coreDevice.SentCommands.Clear();

        // Act
        _device.SetChannelOutputValue(channel, 0);

        // Assert
        Assert.IsFalse(channel.CoreChannel.OutputValue, "Core should mirror the commanded LOW state");
        Assert.AreEqual(1, _coreDevice.SentCommands.Count);
    }

    [TestMethod]
    public void IsDigitalOn_UiPath_DrivesPinThroughCore()
    {
        // Arrange — the drawer/tile toggle binds IsDigitalOn
        var channel = WrapCoreChannel(5);
        channel.Direction = ChannelDirection.Output;
        _coreDevice.SentCommands.Clear();

        // Act
        channel.IsDigitalOn = true;

        // Assert
        Assert.IsTrue(channel.CoreChannel.OutputValue, "UI toggle should reach Core's SetDioValue");
        Assert.AreEqual(1, _coreDevice.SentCommands.Count);
    }

    [TestMethod]
    public void SetChannelDirection_DelegatesToCore()
    {
        // Arrange
        var channel = WrapCoreChannel(2);
        _coreDevice.SentCommands.Clear();

        // Act — the drawer INPUT/OUTPUT toggle binds IsOutput, which sets Direction
        channel.IsOutput = true;

        // Assert
        Assert.AreEqual(ChannelDirection.Output, channel.CoreChannel.Direction);
        Assert.AreEqual(1, _coreDevice.SentCommands.Count, "Direction change should send one device command");
    }

    [TestMethod]
    public void SetChannelOutputValue_WhenCoreDeviceMissing_IsSilentNoOp()
    {
        // Arrange — a disconnect can race a UI toggle; the wrapper must not throw (issue #619)
        var channel = WrapCoreChannel(1);
        channel.Direction = ChannelDirection.Output;
        _device.SetCoreDevice(null);
        _coreDevice.SentCommands.Clear();

        // Act
        _device.SetChannelOutputValue(channel, 1);

        // Assert
        Assert.AreEqual(0, _coreDevice.SentCommands.Count, "No command should be sent without a Core device");
    }

    [TestMethod]
    public void SetChannelDirection_WhenDisconnected_IsSilentNoOp()
    {
        // Arrange — Core device present but not connected
        var disconnectedCore = new RecordingCoreDevice();
        disconnectedCore.PopulateChannelsFromStatus(new DaqifiOutMessage
        {
            DevicePn = "Nq1",
            DeviceSn = 12345,
            DeviceFwRev = "1.0.0",
            DigitalPortNum = 16
        });
        _device.SetCoreDevice(disconnectedCore);
        var coreChannel = disconnectedCore.Channels
            .OfType<Daqifi.Core.Channel.IDigitalChannel>()
            .First(c => c.ChannelNumber == 0);
        var channel = new DigitalChannel(_device, coreChannel);

        // Act
        _device.SetChannelDirection(channel, ChannelDirection.Output);

        // Assert
        Assert.AreEqual(0, disconnectedCore.SentCommands.Count, "No command should be sent while disconnected");
    }

    [TestMethod]
    public void SetChannelOutputValue_WhenCoreCommandThrows_LogsAndDoesNotPropagate()
    {
        // Arrange — Core can throw mid-call when a disconnect races the IsConnected guard;
        // the exception must not surface through the WPF-binding entry point (issue #619)
        var channel = WrapCoreChannel(4);
        channel.Direction = ChannelDirection.Output;
        _coreDevice.SentCommands.Clear();
        _coreDevice.ThrowOnSend = true;

        // Act — must not throw
        _device.SetChannelOutputValue(channel, 1);

        // Assert
        Assert.AreEqual(0, _coreDevice.SentCommands.Count, "A failed command must not be recorded");
    }

    [TestMethod]
    public void SetChannelDirection_WhenCoreCommandThrows_LogsAndDoesNotPropagate()
    {
        // Arrange
        var channel = WrapCoreChannel(6);
        _coreDevice.SentCommands.Clear();
        _coreDevice.ThrowOnSend = true;

        // Act — must not throw
        _device.SetChannelDirection(channel, ChannelDirection.Output);

        // Assert
        Assert.AreEqual(0, _coreDevice.SentCommands.Count, "A failed command must not be recorded");
    }

    [TestMethod]
    public void SetChannelOutputValue_NonDigitalChannel_IsIgnored()
    {
        // Arrange
        var coreAnalog = new Daqifi.Core.Channel.AnalogChannel(0, 4096)
        {
            Name = "AI0",
            Direction = ChannelDirection.Input,
            CalibrationB = 0,
            CalibrationM = 1,
            InternalScaleM = 1,
            PortRange = 5
        };
        var analogChannel = new AnalogChannel(_device, coreAnalog);
        _coreDevice.SentCommands.Clear();

        // Act
        _device.SetChannelOutputValue(analogChannel, 1);

        // Assert
        Assert.AreEqual(0, _coreDevice.SentCommands.Count, "Analog channels must not produce DIO commands");
    }

    private sealed class DioTestDevice : AbstractStreamingDevice
    {
        public override ConnectionType ConnectionType => ConnectionType.Usb;

        public override bool Connect() => true;

        public override bool Disconnect() => true;

        public override bool Write(string command) => true;

        protected override void SendMessage(IOutboundMessage<string> message)
        {
        }

        public void SetCoreDevice(CoreStreamingDevice? coreDevice) => CoreDevice = coreDevice;
    }

    private sealed class RecordingCoreDevice() : CoreStreamingDevice("DioCoreTestDevice")
    {
        public List<string> SentCommands { get; } = [];

        public bool ThrowOnSend { get; set; }

        public override void Send<T>(IOutboundMessage<T> message)
        {
            if (ThrowOnSend)
            {
                throw new InvalidOperationException("Simulated transport failure.");
            }

            if (message is IOutboundMessage<string> stringMessage)
            {
                SentCommands.Add(stringMessage.Data);
            }
        }
    }
}
