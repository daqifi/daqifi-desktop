using Daqifi.Core.Device.Discovery;
using Daqifi.Desktop;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.ViewModels;
using Moq;
using System.Reflection;

namespace Daqifi.Desktop.Test.ViewModels;

[TestClass]
public class ConnectionDialogViewModelSerialDiscoveryTests
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
    public void AddSerialDeviceFromDiscovery_UsesCoreDeviceMetadata()
    {
        // Arrange
        var viewModel = CreateViewModel();
        var deviceInfo = new DeviceInfo
        {
            Name = "NQ1-USB",
            SerialNumber = "DAQ-12345",
            FirmwareVersion = "1.2.3",
            ConnectionType = ConnectionType.Serial,
            PortName = "COM7"
        };

        // Act
        InvokeAddSerialDeviceFromDiscovery(viewModel, deviceInfo);

        // Assert
        Assert.AreEqual(1, viewModel.AvailableSerialDevices.Count);
        Assert.IsFalse(viewModel.HasNoSerialDevices);

        var serialDevice = viewModel.AvailableSerialDevices[0];
        Assert.AreEqual("NQ1-USB", serialDevice.Name);
        Assert.AreEqual("DAQ-12345", serialDevice.DeviceSerialNo);
        Assert.AreEqual("1.2.3", serialDevice.DeviceVersion);
        Assert.AreEqual("COM7", serialDevice.PortName);
    }

    [TestMethod]
    public void AddSerialDeviceFromDiscovery_DoesNotAddDuplicatePorts()
    {
        // Arrange
        var viewModel = CreateViewModel();
        var firstDevice = new DeviceInfo
        {
            Name = "First Device",
            SerialNumber = "DAQ-12345",
            FirmwareVersion = "1.0.0",
            ConnectionType = ConnectionType.Serial,
            PortName = "COM7"
        };
        var duplicatePortDevice = new DeviceInfo
        {
            Name = "Second Device",
            SerialNumber = "DAQ-67890",
            FirmwareVersion = "2.0.0",
            ConnectionType = ConnectionType.Serial,
            PortName = "COM7"
        };

        // Act
        InvokeAddSerialDeviceFromDiscovery(viewModel, firstDevice);
        InvokeAddSerialDeviceFromDiscovery(viewModel, duplicatePortDevice);

        // Assert
        Assert.AreEqual(1, viewModel.AvailableSerialDevices.Count);
        Assert.AreEqual("Second Device", viewModel.AvailableSerialDevices[0].Name);
        Assert.AreEqual("DAQ-67890", viewModel.AvailableSerialDevices[0].DeviceSerialNo);
        Assert.AreEqual("2.0.0", viewModel.AvailableSerialDevices[0].DeviceVersion);
    }

    [TestMethod]
    public void AddSerialDeviceFromDiscovery_RefreshesExistingDeviceInPlace()
    {
        // Arrange
        var viewModel = CreateViewModel();
        var initialDevice = new DeviceInfo
        {
            Name = string.Empty,
            SerialNumber = string.Empty,
            FirmwareVersion = string.Empty,
            ConnectionType = ConnectionType.Serial,
            PortName = "COM7"
        };
        var refreshedDevice = new DeviceInfo
        {
            Name = "NQ1-USB",
            SerialNumber = "DAQ-12345",
            FirmwareVersion = "1.2.3",
            ConnectionType = ConnectionType.Serial,
            PortName = "COM7"
        };

        InvokeAddSerialDeviceFromDiscovery(viewModel, initialDevice);
        var existingDevice = viewModel.AvailableSerialDevices[0];

        // Act
        InvokeAddSerialDeviceFromDiscovery(viewModel, refreshedDevice);

        // Assert
        Assert.AreEqual(1, viewModel.AvailableSerialDevices.Count);
        Assert.AreSame(existingDevice, viewModel.AvailableSerialDevices[0]);
        Assert.AreEqual("NQ1-USB", existingDevice.Name);
        Assert.AreEqual("DAQ-12345", existingDevice.DeviceSerialNo);
        Assert.AreEqual("1.2.3", existingDevice.DeviceVersion);
    }

    private static ConnectionDialogViewModel CreateViewModel()
    {
        var dialogService = new Mock<IDialogService>();
        return new ConnectionDialogViewModel(dialogService.Object);
    }

    private static void InvokeAddSerialDeviceFromDiscovery(
        ConnectionDialogViewModel viewModel,
        IDeviceInfo deviceInfo)
    {
        var method = typeof(ConnectionDialogViewModel).GetMethod(
            "AddSerialDeviceFromDiscovery",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.IsNotNull(method);
        method.Invoke(viewModel, [deviceInfo]);
    }
}
