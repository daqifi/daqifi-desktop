using System.Collections.Generic;
using Daqifi.Core.Communication.Messages;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.ViewModels;
using DeviceType = Daqifi.Core.Device.DeviceType;

namespace Daqifi.Desktop.Test.ViewModels;

/// <summary>
/// Verifies the device-tile presentation of the detected device type (issue #258):
/// the tile maps <see cref="DeviceType"/> to a user-friendly label and refreshes it
/// in place when the underlying device reports a newly-detected type.
/// </summary>
[TestClass]
public class DeviceTileViewModelTests
{
    [TestMethod]
    [DataRow(DeviceType.Nyquist1, "Nyquist 1")]
    [DataRow(DeviceType.Nyquist2, "Nyquist 2")]
    [DataRow(DeviceType.Nyquist3, "Nyquist 3")]
    [DataRow(DeviceType.Unknown, "Unknown")]
    public void DeviceTypeDisplay_MapsEachTypeToFriendlyName(DeviceType type, string expected)
    {
        var device = new TileTestDevice { DeviceType = type };
        using var tile = new DeviceTileViewModel(device);

        Assert.AreEqual(expected, tile.DeviceTypeDisplay);
    }

    [TestMethod]
    public void DeviceTypeDisplay_DefaultsToUnknown_BeforeDetection()
    {
        var device = new TileTestDevice();
        using var tile = new DeviceTileViewModel(device);

        Assert.AreEqual("Unknown", tile.DeviceTypeDisplay);
    }

    [TestMethod]
    public void DeviceType_Change_RaisesDeviceTypeDisplayPropertyChanged_AndUpdatesValue()
    {
        var device = new TileTestDevice();
        using var tile = new DeviceTileViewModel(device);
        var changed = new List<string>();
        tile.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        device.DeviceType = DeviceType.Nyquist3;

        CollectionAssert.Contains(changed, nameof(DeviceTileViewModel.DeviceTypeDisplay),
            "Tile must refresh DeviceTypeDisplay when the device reports a new type");
        Assert.AreEqual("Nyquist 3", tile.DeviceTypeDisplay);
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
