using Daqifi.Core.Device.Discovery;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.ViewModels;
using Moq;
using System.Reflection;

namespace Daqifi.Desktop.Test.ViewModels;

[TestClass]
public class ConnectionDialogViewModelSerialDiscoveryTests
{
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
        Assert.AreEqual("First Device", viewModel.AvailableSerialDevices[0].Name);
        Assert.AreEqual("DAQ-12345", viewModel.AvailableSerialDevices[0].DeviceSerialNo);
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
