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

    private sealed class TileTestDevice : AbstractStreamingDevice
    {
        public override ConnectionType ConnectionType => ConnectionType.Usb;

        public override bool Connect() => true;

        public override bool Disconnect() => true;

        public override bool Write(string command) => true;

        protected override void SendMessage(IOutboundMessage<string> message)
        {
        }
    }
}
