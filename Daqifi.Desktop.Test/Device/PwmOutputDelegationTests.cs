using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Device;
using Daqifi.Core.Communication.Messages;
using ChannelDirection = Daqifi.Core.Channel.ChannelDirection;
using CoreStreamingDevice = Daqifi.Core.Device.DaqifiStreamingDevice;

namespace Daqifi.Desktop.Test.Device;

/// <summary>
/// Verifies that PWM commands delegate to Core's SetPwmEnabled / SetPwmDutyCycle /
/// SetPwmFrequency (issue #664) in Core's documented duty → frequency → enable order,
/// that duty edits are bookkeeping-only until enabled and live afterwards, and that the
/// log-and-no-op-when-disconnected wrapper semantics are preserved (Core throws when
/// disconnected; the desktop wrapper must not, see issue #619).
/// </summary>
[TestClass]
public class PwmOutputDelegationTests
{
    private PwmTestDevice _device;
    private RecordingCoreDevice _coreDevice;

    [TestInitialize]
    public void Setup()
    {
        _device = new PwmTestDevice();
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
    public void Construction_SeedsDefaultDuty_ForPwmCapableChannels()
    {
        // Act — Core's bookkeeping starts at 0, which Core rejects as a command
        var capable = WrapCoreChannel(4);
        var nonCapable = WrapCoreChannel(1);

        // Assert — capable channels get a commandable default; the seed sends nothing
        Assert.AreEqual(50, capable.PwmDutyCyclePercent, "Capable channels should seed a usable default duty");
        Assert.AreEqual(0, nonCapable.PwmDutyCyclePercent, "Non-capable channels keep Core's zero bookkeeping");
        Assert.AreEqual(0, _coreDevice.SentCommands.Count, "Seeding the duty default must not issue device commands");
    }

    [TestMethod]
    public void SetChannelPwmEnabled_Enable_SendsDutyThenFrequencyThenEnable()
    {
        // Arrange
        var channel = WrapCoreChannel(4);
        channel.PwmDutyCyclePercent = 30;
        _coreDevice.SentCommands.Clear();

        // Act
        _device.SetChannelPwmEnabled(channel, true);

        // Assert — Core-documented call order (issue #664)
        CollectionAssert.AreEqual(
            new[]
            {
                "PWM:CHannel:DUTY 4,30",
                "PWM:CHannel:FREQuency 0,1000",
                "PWM:CHannel:ENable 4,1"
            },
            _coreDevice.SentCommands,
            $"Expected duty → frequency → enable, got: {string.Join(" | ", _coreDevice.SentCommands)}");
        Assert.IsTrue(channel.IsPwmEnabled, "Core should mirror the enabled state into its bookkeeping");
    }

    [TestMethod]
    public void IsPwmEnabled_UiPath_EnablesThroughCore()
    {
        // Arrange — the drawer PWM toggle binds IsPwmEnabled
        var channel = WrapCoreChannel(5);
        _coreDevice.SentCommands.Clear();

        // Act
        channel.IsPwmEnabled = true;

        // Assert
        Assert.IsTrue(channel.IsPwmEnabled, "UI toggle should reach Core's SetPwmEnabled");
        Assert.AreEqual(3, _coreDevice.SentCommands.Count, "Enable resends duty and frequency first");
        Assert.AreEqual("PWM:CHannel:ENable 5,1", _coreDevice.SentCommands[^1]);
    }

    [TestMethod]
    public void IsPwmEnabled_Disable_SendsSingleDisable_AndKeepsDriveFlagInLockstep()
    {
        // Arrange — an output channel driven HIGH, then switched to PWM
        var channel = WrapCoreChannel(4);
        channel.Direction = ChannelDirection.Output;
        channel.IsDigitalOn = true;
        channel.IsPwmEnabled = true;
        _coreDevice.SentCommands.Clear();

        // Act
        channel.IsPwmEnabled = false;

        // Assert — disable is a single command; Core zeroes the stored output value
        // (the pin goes high-impedance) and the desktop flag must follow without
        // re-driving the pin
        CollectionAssert.AreEqual(
            new[] { "PWM:CHannel:ENable 4,0" },
            _coreDevice.SentCommands);
        Assert.IsFalse(channel.IsPwmEnabled);
        Assert.IsFalse(channel.IsDigitalOn, "Desktop drive flag should follow Core's zeroed output mirror");
    }

    [TestMethod]
    public void PwmDutyCycle_WhileDisabled_UpdatesBookkeepingOnly()
    {
        // Arrange
        var channel = WrapCoreChannel(3);
        _coreDevice.SentCommands.Clear();

        // Act
        channel.PwmDutyCyclePercent = 25;

        // Assert — stored, not commanded; the next enable applies it
        Assert.AreEqual(25, channel.PwmDutyCyclePercent);
        Assert.AreEqual(0, _coreDevice.SentCommands.Count, "Duty edits while disabled must not command the device");

        _device.SetChannelPwmEnabled(channel, true);
        Assert.AreEqual("PWM:CHannel:DUTY 3,25", _coreDevice.SentCommands[0], "The stored duty is applied on enable");
    }

    [TestMethod]
    public void PwmDutyCycle_WhileEnabled_CommandsLive()
    {
        // Arrange
        var channel = WrapCoreChannel(4);
        channel.IsPwmEnabled = true;
        _coreDevice.SentCommands.Clear();

        // Act — the drawer duty slider edits duty while PWM runs
        channel.PwmDutyCyclePercent = 80;

        // Assert
        CollectionAssert.AreEqual(
            new[] { "PWM:CHannel:DUTY 4,80" },
            _coreDevice.SentCommands);
        Assert.AreEqual(80, channel.PwmDutyCyclePercent, "Core should mirror the live duty change");
    }

    [TestMethod]
    public void PwmDutyCycle_CoercesIntoCommandableRange()
    {
        // Arrange
        var channel = WrapCoreChannel(4);
        _coreDevice.SentCommands.Clear();

        // Act + assert — Core rejects 0 and values over 100, so the wrapper coerces
        channel.PwmDutyCyclePercent = 250;
        Assert.AreEqual(100, channel.PwmDutyCyclePercent);

        channel.PwmDutyCyclePercent = 0;
        Assert.AreEqual(1, channel.PwmDutyCyclePercent);
    }

    [TestMethod]
    public void PwmFrequency_Set_CommandsDeviceOnceAndCoerces()
    {
        // Arrange
        _coreDevice.SentCommands.Clear();

        // Act — the drawer's device-wide frequency field
        _device.PwmFrequencyHz = 100;

        // Assert — commanded immediately (a live change rescales enabled channels)
        CollectionAssert.AreEqual(
            new[] { "PWM:CHannel:FREQuency 0,100" },
            _coreDevice.SentCommands);
        Assert.AreEqual(100, _device.PwmFrequencyHz);

        // Act + assert — same value again is not re-commanded
        _device.PwmFrequencyHz = 100;
        Assert.AreEqual(1, _coreDevice.SentCommands.Count);

        // Act + assert — out-of-range edits coerce to Core's documented limits
        _device.PwmFrequencyHz = 5;
        Assert.AreEqual(6, _device.PwmFrequencyHz);
        _device.PwmFrequencyHz = 60_000;
        Assert.AreEqual(50_000, _device.PwmFrequencyHz);
    }

    [TestMethod]
    public void SetChannelPwmEnabled_WhenCoreDeviceMissing_IsSilentNoOp()
    {
        // Arrange — a disconnect can race a UI toggle; the wrapper must not throw
        var channel = WrapCoreChannel(4);
        _device.SetCoreDevice(null);
        _coreDevice.SentCommands.Clear();

        // Act — must not throw
        channel.IsPwmEnabled = true;

        // Assert — nothing sent, and the bookkeeping still reads disabled so the UI snaps back
        Assert.AreEqual(0, _coreDevice.SentCommands.Count);
        Assert.IsFalse(channel.IsPwmEnabled);
    }

    [TestMethod]
    public void PwmFrequency_WhenCoreDeviceMissing_KeepsValueForNextEnable()
    {
        // Arrange
        _device.SetCoreDevice(null);
        var channel = WrapCoreChannel(4);
        _coreDevice.SentCommands.Clear();

        // Act — must not throw; the value is kept locally
        _device.PwmFrequencyHz = 250;
        Assert.AreEqual(250, _device.PwmFrequencyHz);
        Assert.AreEqual(0, _coreDevice.SentCommands.Count);

        // Assert — once the device is back, enable resends the kept frequency
        _device.SetCoreDevice(_coreDevice);
        _device.SetChannelPwmEnabled(channel, true);
        CollectionAssert.Contains(_coreDevice.SentCommands, "PWM:CHannel:FREQuency 0,250");
    }

    [TestMethod]
    public void SetChannelPwmEnabled_WhenCoreCommandThrows_LogsAndDoesNotPropagate()
    {
        // Arrange — Core can throw mid-call when a disconnect races the IsConnected guard
        var channel = WrapCoreChannel(4);
        _coreDevice.SentCommands.Clear();
        _coreDevice.ThrowOnSend = true;

        // Act — must not throw
        channel.IsPwmEnabled = true;

        // Assert — the sequence failed at the first send, so nothing was recorded and the
        // enable bookkeeping was never reached
        Assert.AreEqual(0, _coreDevice.SentCommands.Count);
        Assert.IsFalse(channel.IsPwmEnabled, "A failed enable must leave the channel disabled");
    }

    [TestMethod]
    public void SetChannelPwmEnabled_NonCapableChannel_RejectedByCoreWithoutEnable()
    {
        // Arrange — the UI gates on IsPwmCapable, but the wrapper must still contain
        // Core's guardrail rejection (the firmware would freeze the channel otherwise)
        var channel = WrapCoreChannel(1);
        _coreDevice.SentCommands.Clear();

        // Act — must not throw
        channel.IsPwmEnabled = true;

        // Assert
        Assert.AreEqual(0, _coreDevice.SentCommands.Count, "No PWM command may reach a non-capable channel");
        Assert.IsFalse(channel.IsPwmEnabled);
    }

    [TestMethod]
    public void SetChannelPwmEnabled_NonDigitalChannel_IsIgnored()
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
        _device.SetChannelPwmEnabled(analogChannel, true);
        _device.SetChannelPwmDutyCycle(analogChannel, 50);

        // Assert
        Assert.AreEqual(0, _coreDevice.SentCommands.Count, "Analog channels must not produce PWM commands");
    }

    [TestMethod]
    public void ReplaceCoreChannel_CarriesPwmBookkeepingAcrossRefresh()
    {
        // Arrange — enable through the normal path, then refresh the core channel
        var channel = WrapCoreChannel(4);
        channel.PwmDutyCyclePercent = 30;
        channel.IsPwmEnabled = true;
        var freshCoreChannel = new Daqifi.Core.Channel.DigitalChannel(4, isPwmCapable: true) { Name = "DIO4" };
        _coreDevice.SentCommands.Clear();

        // Act
        channel.ReplaceCoreChannel(freshCoreChannel);

        // Assert — PWM state carried onto the fresh core channel with no device command
        Assert.IsTrue(channel.IsPwmEnabled, "PWM enabled bookkeeping should carry across the refresh");
        Assert.AreEqual(30, channel.PwmDutyCyclePercent, "Duty bookkeeping should carry across the refresh");
        Assert.AreEqual(0, _coreDevice.SentCommands.Count, "A core-channel refresh must not command the device");
    }

    private sealed class PwmTestDevice : AbstractStreamingDevice
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

    private sealed class RecordingCoreDevice() : CoreStreamingDevice("PwmCoreTestDevice")
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
