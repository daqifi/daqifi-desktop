using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.ViewModels;
using Daqifi.Core.Communication.Messages;
using ChannelDirection = Daqifi.Core.Channel.ChannelDirection;
using CoreDigitalChannel = Daqifi.Core.Channel.DigitalChannel;

namespace Daqifi.Desktop.Test.ViewModels;

/// <summary>
/// Verifies the channel-tile presentation logic for digital outputs (issue #663):
/// output tiles surface the last commanded state from the channel instead of the
/// streamed sample (outputs are never sampled), and they do so whether or not the
/// channel is activated for streaming.
/// </summary>
[TestClass]
public class ChannelTileViewModelTests
{
    private TileTestDevice _device;

    [TestInitialize]
    public void Setup()
    {
        _device = new TileTestDevice();
    }

    private DigitalChannel CreateDigitalChannel(int index, ChannelDirection direction)
    {
        var coreChannel = new CoreDigitalChannel(index)
        {
            Name = $"DIO{index}",
            Direction = direction
        };
        return new DigitalChannel(_device, coreChannel);
    }

    /// <summary>
    /// Wraps a PWM-capable channel from a connected no-op Core device and enables PWM
    /// through the real UI path, so later enable/disable/duty edits stick in Core's
    /// bookkeeping and raise the wrapper's change notifications.
    /// </summary>
    private DigitalChannel CreatePwmEnabledChannel(int index, ChannelDirection direction, int dutyPercent)
    {
        var coreDevice = new NoOpCoreDevice();
        coreDevice.PopulateChannelsFromStatus(new DaqifiOutMessage
        {
            DevicePn = "Nq1",
            DeviceSn = 12345,
            DeviceFwRev = "1.0.0",
            DigitalPortNum = 16
        });
        coreDevice.Connect();
        _device.SetCoreDevice(coreDevice);

        var coreChannel = coreDevice.Channels
            .OfType<Daqifi.Core.Channel.IDigitalChannel>()
            .First(c => c.ChannelNumber == index);
        coreChannel.Direction = direction;

        var channel = new DigitalChannel(_device, coreChannel);
        _device.DataChannels.Add(channel);
        channel.PwmDutyCyclePercent = dutyPercent;
        channel.IsPwmEnabled = true;
        return channel;
    }

    private static ChannelTileViewModel CreateTile(IChannel channel) =>
        new(channel, parent: null, deviceName: "TestDevice", showDeviceLabel: false);

    [TestMethod]
    public void OutputTile_ShowsCommandedState_EvenWhenInactive()
    {
        // Arrange
        var channel = CreateDigitalChannel(0, ChannelDirection.Output);
        var tile = CreateTile(channel);

        // Assert — the pin is driven regardless of streaming activation
        Assert.IsTrue(tile.IsDigitalOutput);
        Assert.IsTrue(tile.ShowValue, "Output tiles always show the commanded state");
        Assert.AreEqual("LOW", tile.Value, "Uncommanded output should read LOW");
    }

    [TestMethod]
    public void OutputTile_TracksCommandedStateChanges()
    {
        // Arrange
        var channel = CreateDigitalChannel(0, ChannelDirection.Output);
        var tile = CreateTile(channel);
        var changedProperties = new List<string?>();
        tile.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        // Act
        channel.IsDigitalOn = true;

        // Assert
        Assert.AreEqual("HIGH", tile.Value, "Tile should show the newly commanded state");
        CollectionAssert.Contains(changedProperties, nameof(ChannelTileViewModel.Value),
            "Tile must raise PropertyChanged for Value when the commanded state flips");
    }

    [TestMethod]
    public void OutputTile_IgnoresStreamedSample()
    {
        // Arrange — a stale sample must not override the commanded state
        var channel = CreateDigitalChannel(0, ChannelDirection.Output);
        channel.IsActive = true;
        channel.ActiveSample = new DataSample(_device, channel, DateTime.Now, 1);
        var tile = CreateTile(channel);

        // Assert — commanded LOW wins over the sampled 1
        Assert.AreEqual("LOW", tile.Value);
    }

    [TestMethod]
    public void InputTile_HidesValueWhenInactive()
    {
        // Arrange
        var channel = CreateDigitalChannel(0, ChannelDirection.Input);
        var tile = CreateTile(channel);

        // Assert
        Assert.IsFalse(tile.IsDigitalOutput);
        Assert.IsFalse(tile.ShowValue, "Inactive input tiles hide the value line");
        Assert.IsNull(tile.Value);
    }

    [TestMethod]
    public void InputTile_ShowsStreamedValueWhenActive()
    {
        // Arrange
        var channel = CreateDigitalChannel(0, ChannelDirection.Input);
        channel.IsActive = true;
        var tile = CreateTile(channel);

        // Assert — no sample yet
        Assert.IsTrue(tile.ShowValue);
        Assert.AreEqual("—", tile.Value);

        // Act
        channel.ActiveSample = new DataSample(_device, channel, DateTime.Now, 1);

        // Assert
        Assert.AreEqual("HIGH", tile.Value);
    }

    [TestMethod]
    public void DirectionFlip_UpdatesOutputPresentation()
    {
        // Arrange
        var channel = CreateDigitalChannel(0, ChannelDirection.Input);
        var tile = CreateTile(channel);

        // Act — the drawer INPUT/OUTPUT toggle binds IsOutput
        channel.IsOutput = true;

        // Assert
        Assert.IsTrue(tile.IsDigitalOutput);
        Assert.IsTrue(tile.ShowValue);
        Assert.AreEqual("DIGITAL OUT", tile.TypeLabel);
        Assert.AreEqual("LOW", tile.Value);
    }

    [TestMethod]
    public void PwmTile_ShowsDutyAndShelvesAsOutput_EvenInInputDirection()
    {
        // Arrange — PWM drives the pin regardless of the stored direction (issue #664)
        var channel = CreatePwmEnabledChannel(4, ChannelDirection.Input, dutyPercent: 45);
        var tile = CreateTile(channel);

        // Assert
        Assert.IsTrue(tile.IsPwmActive);
        Assert.IsTrue(tile.IsDigitalOutput, "A PWM-active channel shelves as a digital output");
        Assert.AreEqual("DIGITAL OUT", tile.TypeLabel);
        Assert.IsTrue(tile.ShowValue);
        Assert.AreEqual("PWM 45%", tile.Value, "The value line shows the commanded duty");
    }

    [TestMethod]
    public void PwmTile_SuppressesDriveToggle_WhileOutputTileKeepsIt()
    {
        // Arrange — the hardware ignores digital state writes while PWM runs
        var pwmTile = CreateTile(CreatePwmEnabledChannel(4, ChannelDirection.Output, dutyPercent: 45));
        var plainOutputTile = CreateTile(CreateDigitalChannel(2, ChannelDirection.Output));

        // Assert
        Assert.IsFalse(pwmTile.ShowDriveToggle, "PWM-active tiles must hide the drive toggle");
        Assert.IsTrue(plainOutputTile.ShowDriveToggle, "Plain output tiles keep the drive toggle");
        Assert.AreEqual("PWM 45%", pwmTile.Value, "The duty wins over the commanded HIGH/LOW state");
    }

    [TestMethod]
    public void PwmTile_DutyChange_RaisesValueChanged()
    {
        // Arrange
        var channel = CreatePwmEnabledChannel(4, ChannelDirection.Input, dutyPercent: 45);
        var tile = CreateTile(channel);
        var changedProperties = new List<string?>();
        tile.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        // Act — live duty edit from the drawer slider
        channel.PwmDutyCyclePercent = 80;

        // Assert
        Assert.AreEqual("PWM 80%", tile.Value, "Tile should show the live duty change");
        CollectionAssert.Contains(changedProperties, nameof(ChannelTileViewModel.Value),
            "Tile must raise PropertyChanged for Value when the duty changes");
    }

    [TestMethod]
    public void PwmDisable_RestoresDigitalInputPresentation()
    {
        // Arrange
        var channel = CreatePwmEnabledChannel(4, ChannelDirection.Input, dutyPercent: 45);
        var tile = CreateTile(channel);
        var changedProperties = new List<string?>();
        tile.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        // Act — disable through the same UI path the drawer toggle uses
        channel.IsPwmEnabled = false;

        // Assert — the tile reads as a plain inactive digital input again
        Assert.IsFalse(tile.IsPwmActive);
        Assert.IsFalse(tile.IsDigitalOutput);
        Assert.AreEqual("DIGITAL IN", tile.TypeLabel);
        Assert.IsFalse(tile.ShowValue);
        Assert.IsNull(tile.Value);
        CollectionAssert.Contains(changedProperties, nameof(ChannelTileViewModel.IsDigitalOutput),
            "Tile must refresh its shelving flags when PWM turns off");
    }

    private sealed class TileTestDevice : AbstractStreamingDevice
    {
        public override ConnectionType ConnectionType => ConnectionType.Usb;

        public override bool Connect() => true;

        public override bool Disconnect() => true;

        public override bool Write(string command) => true;

        protected override void SendMessage(IOutboundMessage<string> message)
        {
        }

        public void SetCoreDevice(Daqifi.Core.Device.DaqifiStreamingDevice? coreDevice) =>
            CoreDevice = coreDevice;
    }

    private sealed class NoOpCoreDevice() : Daqifi.Core.Device.DaqifiStreamingDevice("TileCoreTestDevice")
    {
        public override void Send<T>(IOutboundMessage<T> message)
        {
        }
    }
}
