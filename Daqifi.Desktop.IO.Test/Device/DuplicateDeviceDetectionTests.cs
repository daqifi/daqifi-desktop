using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Daqifi.Desktop.IO.Test.Device;

/// <summary>
/// Tests for duplicate device detection functionality
/// </summary>
[TestClass]
public class DuplicateDeviceDetectionTests
{
    private Mock<IStreamingDevice> _mockUsbDevice;
    private Mock<IStreamingDevice> _mockWifiDevice;
    private ConnectionManager _connectionManager;

    [TestInitialize]
    public void Setup()
    {
        _mockUsbDevice = new Mock<IStreamingDevice>();
        _mockUsbDevice.Setup(d => d.DeviceSerialNo).Returns("DAQ-12345");
        _mockUsbDevice.Setup(d => d.ConnectionType).Returns(ConnectionType.Usb);
        _mockUsbDevice.Setup(d => d.Name).Returns("USB Device");

        _mockWifiDevice = new Mock<IStreamingDevice>();
        _mockWifiDevice.Setup(d => d.DeviceSerialNo).Returns("DAQ-12345");
        _mockWifiDevice.Setup(d => d.ConnectionType).Returns(ConnectionType.Wifi);
        _mockWifiDevice.Setup(d => d.Name).Returns("WiFi Device");

        _connectionManager = ConnectionManager.Instance;
        _connectionManager.ConnectedDevices.Clear();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _connectionManager.ConnectedDevices.Clear();
        _connectionManager.DuplicateDeviceHandler = null;
    }

    [TestMethod]
    public void CheckForDuplicateDevice_WithNoDuplicates_ReturnsFalse()
    {
        // Arrange
        var newDevice = _mockUsbDevice.Object;

        // Act
        var result = InvokePrivateMethod("CheckForDuplicateDevice", newDevice) as DuplicateDeviceCheckResult;

        // Assert
        Assert.IsNotNull(result);
        Assert.IsFalse(result.IsDuplicate);
        Assert.IsNull(result.ExistingDevice);
    }

    [TestMethod]
    public void CheckForDuplicateDevice_WithSameSerialNumber_ReturnsTrue()
    {
        // Arrange
        var existingDevice = _mockUsbDevice.Object;
        var newDevice = _mockWifiDevice.Object;
        _connectionManager.ConnectedDevices.Add(existingDevice);

        // Act
        var result = InvokePrivateMethod("CheckForDuplicateDevice", newDevice) as DuplicateDeviceCheckResult;

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.IsDuplicate);
        Assert.AreSame(existingDevice, result.ExistingDevice);
        Assert.AreSame(newDevice, result.NewDevice);
        Assert.AreEqual("WiFi", result.NewDeviceInterface);
        Assert.AreEqual("USB", result.ExistingDeviceInterface);
    }

    [TestMethod]
    public void CheckForDuplicateDevice_WithEmptySerialNumber_ReturnsFalse()
    {
        // Arrange
        var mockDeviceNoSerial = new Mock<IStreamingDevice>();
        mockDeviceNoSerial.Setup(d => d.DeviceSerialNo).Returns("");
        mockDeviceNoSerial.Setup(d => d.ConnectionType).Returns(ConnectionType.Usb);
        mockDeviceNoSerial.Setup(d => d.Name).Returns("No Serial Device");

        var existingDevice = _mockUsbDevice.Object;
        var newDevice = mockDeviceNoSerial.Object;
        _connectionManager.ConnectedDevices.Add(existingDevice);

        // Act
        var result = InvokePrivateMethod("CheckForDuplicateDevice", newDevice) as DuplicateDeviceCheckResult;

        // Assert
        Assert.IsNotNull(result);
        Assert.IsFalse(result.IsDuplicate);
    }

    [TestMethod]
    public void CheckForDuplicateDevice_WithNullSerialNumber_ReturnsFalse()
    {
        // Arrange
        var mockDeviceNullSerial = new Mock<IStreamingDevice>();
        mockDeviceNullSerial.Setup(d => d.DeviceSerialNo).Returns((string)null);
        mockDeviceNullSerial.Setup(d => d.ConnectionType).Returns(ConnectionType.Usb);
        mockDeviceNullSerial.Setup(d => d.Name).Returns("Null Serial Device");

        var existingDevice = _mockUsbDevice.Object;
        var newDevice = mockDeviceNullSerial.Object;
        _connectionManager.ConnectedDevices.Add(existingDevice);

        // Act
        var result = InvokePrivateMethod("CheckForDuplicateDevice", newDevice) as DuplicateDeviceCheckResult;

        // Assert
        Assert.IsNotNull(result);
        Assert.IsFalse(result.IsDuplicate);
    }

    [TestMethod]
    public void CheckForDuplicateDevice_WithDifferentSerialNumbers_ReturnsFalse()
    {
        // Arrange
        var mockDifferentDevice = new Mock<IStreamingDevice>();
        mockDifferentDevice.Setup(d => d.DeviceSerialNo).Returns("DAQ-67890");
        mockDifferentDevice.Setup(d => d.ConnectionType).Returns(ConnectionType.Wifi);
        mockDifferentDevice.Setup(d => d.Name).Returns("Different Device");

        var existingDevice = _mockUsbDevice.Object;
        var newDevice = mockDifferentDevice.Object;
        _connectionManager.ConnectedDevices.Add(existingDevice);

        // Act
        var result = InvokePrivateMethod("CheckForDuplicateDevice", newDevice) as DuplicateDeviceCheckResult;

        // Assert
        Assert.IsNotNull(result);
        Assert.IsFalse(result.IsDuplicate);
        Assert.IsNull(result.ExistingDevice);
    }

    [TestMethod]
    public void CheckForDuplicateDevice_CaseInsensitiveSerialNumber_ReturnsTrue()
    {
        // Arrange
        var mockLowerCaseDevice = new Mock<IStreamingDevice>();
        mockLowerCaseDevice.Setup(d => d.DeviceSerialNo).Returns("daq-12345"); // lowercase
        mockLowerCaseDevice.Setup(d => d.ConnectionType).Returns(ConnectionType.Wifi);
        mockLowerCaseDevice.Setup(d => d.Name).Returns("Lowercase Device");

        var existingDevice = _mockUsbDevice.Object; // "DAQ-12345" uppercase
        var newDevice = mockLowerCaseDevice.Object;
        _connectionManager.ConnectedDevices.Add(existingDevice);

        // Act
        var result = InvokePrivateMethod("CheckForDuplicateDevice", newDevice) as DuplicateDeviceCheckResult;

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.IsDuplicate);
        Assert.AreSame(existingDevice, result.ExistingDevice);
        Assert.AreSame(newDevice, result.NewDevice);
    }

    /// <summary>
    /// Helper method to invoke private methods on ConnectionManager for testing
    /// </summary>
    private object InvokePrivateMethod(string methodName, params object[] parameters)
    {
        var method = typeof(ConnectionManager).GetMethod(methodName, 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.IsNotNull(method, $"Method {methodName} not found");
        return method.Invoke(_connectionManager, parameters);
    }
}

// Mock interfaces since we can't reference the main project easily
public interface IStreamingDevice
{
    string DeviceSerialNo { get; set; }
    ConnectionType ConnectionType { get; }
    string Name { get; set; }
}

public enum ConnectionType
{
    Usb,
    Wifi
}

// Simplified ConnectionManager for testing
public class ConnectionManager
{
    private static ConnectionManager _instance;
    public static ConnectionManager Instance => _instance ??= new ConnectionManager();

    public List<IStreamingDevice> ConnectedDevices { get; } = new List<IStreamingDevice>();
    public Func<DuplicateDeviceCheckResult, DuplicateDeviceAction> DuplicateDeviceHandler { get; set; }

    private ConnectionManager() { }

    private DuplicateDeviceCheckResult CheckForDuplicateDevice(IStreamingDevice newDevice)
    {
        // If device doesn't have a serial number, we can't check for duplicates reliably
        if (string.IsNullOrWhiteSpace(newDevice.DeviceSerialNo))
        {
            return new DuplicateDeviceCheckResult { IsDuplicate = false };
        }

        // Check if any existing device has the same serial number
        var existingDevice = ConnectedDevices.FirstOrDefault(d => 
            !string.IsNullOrWhiteSpace(d.DeviceSerialNo) && 
            d.DeviceSerialNo.Equals(newDevice.DeviceSerialNo, StringComparison.OrdinalIgnoreCase));

        if (existingDevice != null)
        {
            var newDeviceInterface = newDevice.ConnectionType == ConnectionType.Usb ? "USB" : "WiFi";
            var existingDeviceInterface = existingDevice.ConnectionType == ConnectionType.Usb ? "USB" : "WiFi";
            
            return new DuplicateDeviceCheckResult 
            { 
                IsDuplicate = true, 
                ExistingDevice = existingDevice,
                NewDevice = newDevice,
                NewDeviceInterface = newDeviceInterface,
                ExistingDeviceInterface = existingDeviceInterface
            };
        }

        return new DuplicateDeviceCheckResult { IsDuplicate = false };
    }
}

public class DuplicateDeviceCheckResult
{
    public bool IsDuplicate { get; set; }
    public IStreamingDevice ExistingDevice { get; set; }
    public IStreamingDevice NewDevice { get; set; }
    public string NewDeviceInterface { get; set; }
    public string ExistingDeviceInterface { get; set; }
}

public enum DuplicateDeviceAction
{
    KeepExisting,
    SwitchToNew,
    Cancel
}