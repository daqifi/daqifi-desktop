using Daqifi.Core.Device.Discovery;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.ViewModels;
using Moq;
using System.Net;
using System.Reflection;

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

    [TestMethod]
    public async Task StartWiFiDiscovery_ClearsAvailableWiFiDevicesFromPriorSession()
    {
        // Arrange: populate the list as if a device was found before a discovery restart
        // (e.g. the firmware-flash resume path recreates the finder).
        var viewModel = CreateViewModel();
        var deviceInfo = new DeviceInfo
        {
            Name = "NQ1-WiFi",
            MacAddress = "00:11:22:33:44:55",
            IPAddress = IPAddress.Parse("192.168.1.100"),
            Port = 9760,
            ConnectionType = ConnectionType.WiFi
        };
        viewModel.HandleCoreWifiDeviceDiscovered(null, new DeviceDiscoveredEventArgs(deviceInfo));
        Assert.HasCount(1, viewModel.AvailableWiFiDevices);
        Assert.IsFalse(viewModel.HasNoWiFiDevices);

        try
        {
            // Act: restart discovery (simulates the firmware-flash resume path recreating the finder)
            InvokeStartWiFiDiscovery(viewModel);

            // Assert: the list is reset synchronously so a rediscovery under the new finder's own
            // per-session MAC dedup can't be re-added as a duplicate.
            Assert.IsEmpty(viewModel.AvailableWiFiDevices);
            Assert.IsTrue(viewModel.HasNoWiFiDevices);
        }
        finally
        {
            // Drain the discovery loop kicked off above (rather than the non-async stop, which
            // cancels/disposes without awaiting the running task) so no background discovery work
            // is still in flight once the test completes.
            await InvokeStopWiFiDiscoveryAsync(viewModel);
        }
    }

    private static ConnectionDialogViewModel CreateViewModel()
    {
        var dialogService = new Mock<IDialogService>();
        return new ConnectionDialogViewModel(dialogService.Object);
    }

    private static void InvokeStartWiFiDiscovery(ConnectionDialogViewModel viewModel)
    {
        var method = typeof(ConnectionDialogViewModel).GetMethod(
            "StartWiFiDiscovery",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.IsNotNull(method);
        method.Invoke(viewModel, null);
    }

    private static Task InvokeStopWiFiDiscoveryAsync(ConnectionDialogViewModel viewModel)
    {
        var method = typeof(ConnectionDialogViewModel).GetMethod(
            "StopWiFiDiscoveryAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.IsNotNull(method);
        return (Task)method.Invoke(viewModel, null)!;
    }
}
