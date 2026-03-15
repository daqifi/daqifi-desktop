using Daqifi.Desktop.Device;
using Daqifi.Desktop.Device.SerialDevice;
using Daqifi.Desktop.Device.WiFiDevice;
using System.ComponentModel;
using System.IO.Ports;
using CoreDeviceInfo = Daqifi.Core.Device.Discovery.DeviceInfo;
using CoreConnectionType = Daqifi.Core.Device.Discovery.ConnectionType;
using System.Net;

namespace Daqifi.Desktop.Test.Device;

[TestClass]
public class DisplayIdentifierTests
{
    #region SerialStreamingDevice Tests
    
    [TestMethod]
    public void SerialStreamingDevice_DisplayIdentifier_ShouldReturnComPort()
    {
        // Arrange
        const string expectedComPort = "COM3";
        var device = new SerialStreamingDevice(expectedComPort);
        
        // Act
        var displayIdentifier = device.DisplayIdentifier;
        
        // Assert
        Assert.AreEqual(expectedComPort, displayIdentifier, 
            "USB device should display COM port name");
    }
    
    [TestMethod]
    public void SerialStreamingDevice_DisplayIdentifier_ShouldReturnUsbWhenPortIsNull()
    {
        // Arrange
        var device = new SerialStreamingDevice("COM1");
        device.Port = null; // Simulate null port
        
        // Act
        var displayIdentifier = device.DisplayIdentifier;
        
        // Assert
        Assert.AreEqual("USB", displayIdentifier, 
            "USB device with null port should return 'USB' as fallback");
    }
    
    [TestMethod] 
    public void SerialStreamingDevice_PropertyChanged_ShouldTriggerDisplayIdentifierUpdate()
    {
        // Arrange
        var device = new SerialStreamingDevice("COM1");
        var propertyChangedFired = false;
        string? changedPropertyName = null;
        
        device.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(device.DisplayIdentifier))
            {
                propertyChangedFired = true;
                changedPropertyName = e.PropertyName;
            }
        };
        
        // Act
        device.Port = new System.IO.Ports.SerialPort("COM5");
        
        // Assert
        Assert.IsTrue(propertyChangedFired, "PropertyChanged should fire for DisplayIdentifier when Port changes");
        Assert.AreEqual(nameof(device.DisplayIdentifier), changedPropertyName);
        Assert.AreEqual("COM5", device.DisplayIdentifier);
    }
    
    [TestMethod]
    public void SerialStreamingDevice_ConnectionType_ShouldBeUsb()
    {
        // Arrange
        var device = new SerialStreamingDevice("COM1");
        
        // Act & Assert
        Assert.AreEqual(ConnectionType.Usb, device.ConnectionType,
            "SerialStreamingDevice should have USB connection type");
    }
    
    #endregion
    
    #region DaqifiStreamingDevice (WiFi) Tests
    
    [TestMethod]
    public void DaqifiStreamingDevice_DisplayIdentifier_ShouldReturnIpAddress()
    {
        // Arrange
        const string expectedIpAddress = "192.168.1.100";
        var deviceInfo = new CoreDeviceInfo
        {
            Name = "Test Device",
            IPAddress = IPAddress.Parse(expectedIpAddress),
            MacAddress = "00:11:22:33:44:55",
            SerialNumber = "12345",
            FirmwareVersion = "1.0.0",
            Port = 1234,
            IsPowerOn = true,
            ConnectionType = CoreConnectionType.WiFi
        };
        var device = new DaqifiStreamingDevice(deviceInfo);
        
        // Act
        var displayIdentifier = device.DisplayIdentifier;
        
        // Assert
        Assert.AreEqual(expectedIpAddress, displayIdentifier,
            "WiFi device should display IP address");
    }
    
    [TestMethod]
    public void DaqifiStreamingDevice_ConnectionType_ShouldBeWifi()
    {
        // Arrange
        var deviceInfo = new CoreDeviceInfo
        {
            Name = "Test Device",
            IPAddress = IPAddress.Parse("192.168.1.100"),
            MacAddress = "00:11:22:33:44:55",
            SerialNumber = "12345",
            FirmwareVersion = "1.0.0",
            Port = 1234,
            IsPowerOn = true,
            ConnectionType = CoreConnectionType.WiFi
        };
        var device = new DaqifiStreamingDevice(deviceInfo);
        
        // Act & Assert
        Assert.AreEqual(ConnectionType.Wifi, device.ConnectionType,
            "DaqifiStreamingDevice should have WiFi connection type");
    }
    
    [TestMethod]
    public void DaqifiStreamingDevice_DisplayIdentifier_ShouldReturnEmptyWhenIpAddressClearedAfterConstruction()
    {
        // Arrange - construct with a valid IP (required by constructor), then clear it
        // to simulate a device whose address is unset at display time.
        var deviceInfo = new CoreDeviceInfo
        {
            Name = "Test Device",
            IPAddress = IPAddress.Parse("192.168.1.100"),
            MacAddress = "00:11:22:33:44:55",
            SerialNumber = "12345",
            FirmwareVersion = "1.0.0",
            Port = 1234,
            IsPowerOn = true,
            ConnectionType = CoreConnectionType.WiFi
        };
        var device = new DaqifiStreamingDevice(deviceInfo);
        device.IpAddress = string.Empty;

        // Act
        var displayIdentifier = device.DisplayIdentifier;

        // Assert
        Assert.AreEqual("", displayIdentifier,
            "WiFi device should return empty string when IpAddress is empty");
    }
    
    [TestMethod]
    public void DaqifiStreamingDevice_PropertyChanged_ShouldTriggerDisplayIdentifierUpdate()
    {
        // Arrange
        var deviceInfo = new CoreDeviceInfo
        {
            Name = "Test Device",
            IPAddress = IPAddress.Parse("192.168.1.100"),
            MacAddress = "00:11:22:33:44:55",
            SerialNumber = "12345",
            FirmwareVersion = "1.0.0",
            Port = 1234,
            IsPowerOn = true,
            ConnectionType = CoreConnectionType.WiFi
        };
        var device = new DaqifiStreamingDevice(deviceInfo);
        var propertyChangedFired = false;
        string? changedPropertyName = null;
        
        device.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(device.DisplayIdentifier))
            {
                propertyChangedFired = true;
                changedPropertyName = e.PropertyName;
            }
        };
        
        // Act
        device.IpAddress = "192.168.1.200";
        
        // Assert
        Assert.IsTrue(propertyChangedFired, "PropertyChanged should fire for DisplayIdentifier when IpAddress changes");
        Assert.AreEqual(nameof(device.DisplayIdentifier), changedPropertyName);
        Assert.AreEqual("192.168.1.200", device.DisplayIdentifier);
    }
    
    #endregion
    
    #region Edge Cases
    
    [TestMethod]
    public void DisplayIdentifier_UnknownConnectionType_ShouldReturnUnknown()
    {
        // Arrange - Create a mock device with unknown connection type
        var mockDevice = new TestStreamingDevice(null);
        
        // Act
        var displayIdentifier = mockDevice.DisplayIdentifier;
        
        // Assert
        Assert.AreEqual("Unknown", displayIdentifier,
            "Device with unknown connection type should return 'Unknown'");
    }
    
    #endregion
}

/// <summary>
/// Test implementation of AbstractStreamingDevice for testing purposes
/// </summary>
public class TestStreamingDevice : AbstractStreamingDevice
{
    private readonly ConnectionType? _connectionType;
    
    public TestStreamingDevice(ConnectionType? connectionType)
    {
        _connectionType = connectionType;
    }
    
    public override ConnectionType ConnectionType => _connectionType ?? (ConnectionType)99; // Invalid enum value
    
    public override bool Connect() => true;
    public override bool Disconnect() => true;
    public override bool Write(string command) => true;

    protected override void SendMessage(Daqifi.Core.Communication.Messages.IOutboundMessage<string> message) { }
}
