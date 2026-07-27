using Daqifi.Core.Communication.Messages;
using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Configuration;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.ViewModels;
using Microsoft.EntityFrameworkCore;
using Moq;
using ChannelDirection = Daqifi.Core.Channel.ChannelDirection;
using ChannelType = Daqifi.Core.Channel.ChannelType;

namespace Daqifi.Desktop.Test.ViewModels;

/// <summary>
/// Covers <see cref="ChannelsPaneViewModel"/>'s settings-drawer lifetime across a rebuild
/// (issue #765). The pane rebuilds every tile whenever
/// <see cref="ConnectionManager.ConnectedDevices"/> changes; the drawer is a top-level overlay
/// gated on <c>IsSettingsOpen</c> alone, so nothing about an emptied tile list hides it. It must
/// therefore close itself when the device that owns the channel it is showing goes away, and
/// stay put otherwise.
/// </summary>
[TestClass]
public class ChannelsPaneViewModelTests : IDisposable
{
    #region Setup
    private ChannelsPaneViewModel _viewModel = null!;

    [TestInitialize]
    public void Setup()
    {
        // Other tests Connect() devices onto the process-wide singleton, so reset it here to keep
        // this class order-independent.
        SetConnectedDevices();

        // The parameterless constructor reads LoggingManager.Instance, which resolves its context
        // factory from App.ServiceProvider — absent in the test host. The internal constructor is
        // the seam.
        _viewModel = new ChannelsPaneViewModel(
            new LoggingManager(new Mock<IDbContextFactory<LoggingContext>>().Object));
    }

    [TestCleanup]
    public void Cleanup()
    {
        // Leave the shared singleton clean for whatever test runs next.
        SetConnectedDevices();
    }

    // MSTest disposes the test-class instance after each test, releasing the view-model's refresh
    // timer and singleton subscriptions instead of leaking one per test (CA1001).
    public void Dispose()
    {
        _viewModel.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Publishes a new connected-device list. Assigning the property is what raises
    /// <c>PropertyChanged("ConnectedDevices")</c> — the notification the pane rebuilds on, and the
    /// one <see cref="ConnectionManager.Connect"/> / <see cref="ConnectionManager.Disconnect"/>
    /// raise in production.
    /// </summary>
    private static void SetConnectedDevices(params IStreamingDevice[] devices)
    {
        ConnectionManager.Instance.ConnectedDevices = [.. devices];
    }

    private static PaneTestDevice NewDevice(string name, string serial, params string[] channelNames)
    {
        var device = new PaneTestDevice { Name = name, DeviceSerialNo = serial };
        foreach (var channelName in channelNames)
        {
            device.DataChannels.Add(new FakeChannel { Name = channelName, DeviceSerialNo = serial });
        }
        return device;
    }

    /// <summary>Opens the drawer through the real command path, on the tile for a given channel.</summary>
    private void OpenDrawerOn(IChannel channel)
    {
        var tile = _viewModel.AnalogInputs.FirstOrDefault(t => ReferenceEquals(t.Channel, channel));
        Assert.IsNotNull(tile, "The pane did not build a tile for the channel under test.");
        _viewModel.OpenSettingsCommand.Execute(tile);
        Assert.IsTrue(_viewModel.IsSettingsOpen, "Arrange failed: the drawer did not open.");
    }
    #endregion

    #region Drawer lifetime across a rebuild
    [TestMethod]
    public void OwningDeviceDisconnects_ClosesTheDrawer()
    {
        // Arrange
        var device = NewDevice("Nq1", "SN-0001", "AI0");
        SetConnectedDevices(device);
        OpenDrawerOn(device.DataChannels[0]);

        // Act — the device disappears from the connected list, as Disconnect() makes it.
        SetConnectedDevices();

        // Assert — the drawer cannot be left over an empty pane pointing at removed hardware.
        Assert.IsFalse(_viewModel.IsSettingsOpen);
        Assert.IsNull(_viewModel.SelectedChannel);
        Assert.IsNull(_viewModel.SelectedDevice);
    }

    [TestMethod]
    public void UnrelatedDeviceDisconnects_LeavesTheDrawerOpen()
    {
        // Arrange
        var kept = NewDevice("Nq1", "SN-0001", "AI0");
        var removed = NewDevice("Nq3", "SN-0002", "AI0");
        SetConnectedDevices(kept, removed);
        var openChannel = kept.DataChannels[0];
        OpenDrawerOn(openChannel);

        // Act
        SetConnectedDevices(kept);

        // Assert — a rebuild triggered by someone else's disconnect must not close a drawer the
        // user is working in.
        Assert.IsTrue(_viewModel.IsSettingsOpen);
        Assert.AreSame(openChannel, _viewModel.SelectedChannel);
        Assert.AreSame(kept, _viewModel.SelectedDevice);
    }

    [TestMethod]
    public void AnotherDeviceConnects_LeavesTheDrawerOpen()
    {
        // Arrange
        var first = NewDevice("Nq1", "SN-0001", "AI0");
        SetConnectedDevices(first);
        var openChannel = first.DataChannels[0];
        OpenDrawerOn(openChannel);

        // Act
        SetConnectedDevices(first, NewDevice("Nq3", "SN-0002", "AI0"));

        // Assert
        Assert.IsTrue(_viewModel.IsSettingsOpen);
        Assert.AreSame(openChannel, _viewModel.SelectedChannel);
        Assert.AreSame(first, _viewModel.SelectedDevice);
    }

    [TestMethod]
    public void ClosedDrawer_StaysClosedAcrossARebuild()
    {
        // Arrange
        var device = NewDevice("Nq1", "SN-0001", "AI0");
        SetConnectedDevices(device);

        // Act
        SetConnectedDevices();

        // Assert — the rebuild must not touch drawer state when nothing is open.
        Assert.IsFalse(_viewModel.IsSettingsOpen);
        Assert.IsNull(_viewModel.SelectedChannel);
        Assert.IsNull(_viewModel.SelectedDevice);
    }

    /// <summary>
    /// The pane must still tear the drawer down when the device is dropped by a path that also
    /// empties its channel list — the shape <c>AbstractStreamingDevice.Disconnect</c> leaves behind
    /// (it clears <c>DataChannels</c> to avoid ghost channels on reconnect).
    /// </summary>
    [TestMethod]
    public void OwningDeviceDisconnectsAndClearsItsChannels_ClosesTheDrawer()
    {
        // Arrange
        var kept = NewDevice("Nq1", "SN-0001", "AI0");
        var removed = NewDevice("Nq3", "SN-0002", "AI1");
        SetConnectedDevices(kept, removed);
        OpenDrawerOn(removed.DataChannels[0]);

        // Act
        removed.DataChannels.Clear();
        SetConnectedDevices(kept);

        // Assert
        Assert.IsFalse(_viewModel.IsSettingsOpen);
        Assert.IsNull(_viewModel.SelectedChannel);
        Assert.IsNull(_viewModel.SelectedDevice);
    }
    #endregion

    #region Ownership resolution
    /// <summary>
    /// Ownership is resolved by object identity, not by <see cref="IChannel.DeviceSerialNo"/>.
    /// A blank serial is a real state in this codebase, and matching on it resolves every
    /// blank-serial channel to whichever blank-serial device happens to come first.
    /// </summary>
    [TestMethod]
    public void BlankSerials_ResolveTheDrawerToTheDeviceThatActuallyOwnsTheChannel()
    {
        // Arrange — two devices, neither reporting a serial.
        var first = NewDevice("Nq1", string.Empty, "AI0");
        var second = NewDevice("Nq3", string.Empty, "AI1");
        SetConnectedDevices(first, second);
        var openChannel = second.DataChannels[0];
        OpenDrawerOn(openChannel);

        // Act — a rebuild that changes nothing about either device.
        SetConnectedDevices(first, second);

        // Assert — the drawer's device-scoped half (the device-wide PWM frequency) must point at
        // the real owner, not at the first device with a matching serial string.
        Assert.IsTrue(_viewModel.IsSettingsOpen);
        Assert.AreSame(openChannel, _viewModel.SelectedChannel);
        Assert.AreSame(second, _viewModel.SelectedDevice);
    }
    #endregion

    #region Helper Types
    /// <summary>A no-op streaming device whose channel list the tests own outright.</summary>
    private sealed class PaneTestDevice : AbstractStreamingDevice
    {
        public override ConnectionType ConnectionType => ConnectionType.Usb;

        public override bool Connect() => true;

        public override bool Disconnect() => true;

        public override bool Write(string command) => true;

        protected override void SendMessage(IOutboundMessage<string> message)
        {
        }
    }

    /// <summary>
    /// A hand-rolled analog <see cref="IChannel"/>. The pane only reads presentation state off it,
    /// and a hand-rolled instance keeps reference identity — the thing under test here — obvious.
    /// </summary>
    private sealed class FakeChannel : IChannel
    {
        public int ID { get; set; }
        public string Name { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string DeviceSerialNo { get; set; } = string.Empty;
        public int Index => 0;
        public double OutputValue { get; set; }
        public ChannelType Type => ChannelType.Analog;
        public ChannelDirection Direction { get; set; } = ChannelDirection.Input;
        public string TypeString => Type.ToString();
        public string ScaleExpression { get; set; } = string.Empty;
        public System.Windows.Media.Brush ChannelColorBrush { get; set; } = System.Windows.Media.Brushes.Transparent;
        public bool IsBidirectional { get; set; }
        public bool IsOutput { get; set; }
        public bool HasAdc { get; set; }
        public bool IsActive { get; set; }
        public bool IsDigital => false;
        public bool IsAnalog => true;
        public bool IsDigitalOn { get; set; }
        public bool IsPwmCapable => false;
        public bool IsPwmEnabled { get; set; }
        public int PwmDutyCyclePercent { get; set; }
        public bool IsScalingActive { get; set; }
        public bool HasValidExpression { get; set; }
        public DataSample? ActiveSample { get; set; }
        public bool IsVisible { get; set; } = true;

        public event OnChannelUpdatedHandler? OnChannelUpdated;

        public void NotifyChannelUpdated(object sender, DataSample e) => OnChannelUpdated?.Invoke(sender, e);

        public void SetColor(System.Windows.Media.Brush color) => ChannelColorBrush = color;
    }
    #endregion
}
