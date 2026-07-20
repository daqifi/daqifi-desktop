using System.Collections.Generic;
using System.ComponentModel;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.ViewModels;
using Moq;
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
        // Arrange
        var deviceMock = new Mock<IStreamingDevice>();
        deviceMock.SetupGet(d => d.DeviceType).Returns(type);

        // Act
        using var tile = new DeviceTileViewModel(deviceMock.Object);
        var actual = tile.DeviceTypeDisplay;

        // Assert
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void DeviceTypeDisplay_DefaultsToUnknown_BeforeDetection()
    {
        // Arrange
        var deviceMock = new Mock<IStreamingDevice>();
        deviceMock.SetupGet(d => d.DeviceType).Returns(DeviceType.Unknown);

        // Act
        using var tile = new DeviceTileViewModel(deviceMock.Object);
        var actual = tile.DeviceTypeDisplay;

        // Assert
        Assert.AreEqual("Unknown", actual);
    }

    [TestMethod]
    public void DeviceType_Change_RaisesDeviceTypeDisplayPropertyChanged_AndUpdatesValue()
    {
        // Arrange
        var deviceMock = new Mock<IStreamingDevice>();
        deviceMock.SetupGet(d => d.DeviceType).Returns(DeviceType.Unknown);
        using var tile = new DeviceTileViewModel(deviceMock.Object);
        var changed = new List<string>();
        tile.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        // Act
        deviceMock.SetupGet(d => d.DeviceType).Returns(DeviceType.Nyquist3);
        deviceMock.Raise(d => d.PropertyChanged += null,
            new PropertyChangedEventArgs(nameof(IStreamingDevice.DeviceType)));

        // Assert
        CollectionAssert.Contains(changed, nameof(DeviceTileViewModel.DeviceTypeDisplay),
            "Tile must refresh DeviceTypeDisplay when the device reports a new type");
        Assert.AreEqual("Nyquist 3", tile.DeviceTypeDisplay);
    }
}
