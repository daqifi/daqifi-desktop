using Daqifi.Core.Device.Discovery;
using Daqifi.Desktop;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.ViewModels;
using Moq;
using System.Net;

namespace Daqifi.Desktop.Test.ViewModels;

[TestClass]
public class ConnectionDialogViewModelWifiDiscoveryTests
{
    private Func<DuplicateDeviceCheckResult, DuplicateDeviceAction>? _originalDuplicateDeviceHandler;

    [TestInitialize]
    public void TestInitialize()
    {
        _originalDuplicateDeviceHandler = ConnectionManager.Instance.DuplicateDeviceHandler;
        ConnectionManager.Instance.DuplicateDeviceHandler = null;
    }

    [TestCleanup]
    public void TestCleanup()
    {
        ConnectionManager.Instance.DuplicateDeviceHandler = _originalDuplicateDeviceHandler;
    }

    [TestMethod]
    public void HandleCoreWifiDeviceDiscovered_AddsWifiDeviceUsingCoreDiscoveryMetadata()
    {
        // Arrange
        var viewModel = CreateViewModel();
        var deviceInfo = new DeviceInfo
        {
            Name = "NQ1-WiFi",
            SerialNumber = "DAQ-12345",
            FirmwareVersion = "1.2.3",
            IPAddress = IPAddress.Parse("192.168.1.100"),
            MacAddress = "00:11:22:33:44:55",
            Port = 9760,
            IsPowerOn = true,
            ConnectionType = ConnectionType.WiFi
        };
        var eventArgs = new DeviceDiscoveredEventArgs(deviceInfo);

        // Act
        viewModel.HandleCoreWifiDeviceDiscovered(null, eventArgs);

        // Assert
        Assert.HasCount(1, viewModel.AvailableWiFiDevices);
        Assert.IsFalse(viewModel.HasNoWiFiDevices);

        var wifiDevice = viewModel.AvailableWiFiDevices[0];
        Assert.AreEqual("NQ1-WiFi", wifiDevice.Name);
        Assert.AreEqual("DAQ-12345", wifiDevice.DeviceSerialNo);
        Assert.AreEqual("1.2.3", wifiDevice.DeviceVersion);
        Assert.AreEqual("192.168.1.100", wifiDevice.IpAddress);
        Assert.AreEqual("00:11:22:33:44:55", wifiDevice.MacAddress);
        Assert.AreEqual(9760, wifiDevice.Port);
        Assert.IsTrue(wifiDevice.IsPowerOn);
    }

    private static ConnectionDialogViewModel CreateViewModel()
    {
        var dialogService = new Mock<IDialogService>();
        return new ConnectionDialogViewModel(dialogService.Object);
    }
}
