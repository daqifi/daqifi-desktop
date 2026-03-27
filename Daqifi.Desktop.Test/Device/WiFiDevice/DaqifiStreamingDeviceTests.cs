using System.Net;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Device.WiFiDevice;
using CoreDeviceInfo = Daqifi.Core.Device.Discovery.DeviceInfo;
using CoreConnectionType = Daqifi.Core.Device.Discovery.ConnectionType;

namespace Daqifi.Desktop.Test.Device.WiFiDevice;

[TestClass]
public class DaqifiStreamingDeviceTests
{
    #region Helper Methods

    private static CoreDeviceInfo CreateTestDeviceInfo(
        string name = "Test Device",
        string ipAddress = "192.168.1.100",
        int port = 9760,
        string serialNumber = "12345",
        string firmwareVersion = "3.2.0",
        string macAddress = "00:11:22:33:44:55",
        bool isPowerOn = true)
    {
        return new CoreDeviceInfo
        {
            Name = name,
            IPAddress = IPAddress.Parse(ipAddress),
            Port = port,
            SerialNumber = serialNumber,
            FirmwareVersion = firmwareVersion,
            MacAddress = macAddress,
            IsPowerOn = isPowerOn,
            ConnectionType = CoreConnectionType.WiFi
        };
    }

    #endregion

    #region Constructor Tests

    [TestMethod]
    public void Constructor_ShouldSetPropertiesFromDeviceInfo()
    {
        // Arrange
        var deviceInfo = CreateTestDeviceInfo(
            name: "Nq1",
            ipAddress: "10.0.0.42",
            port: 9760,
            serialNumber: "SN-001",
            firmwareVersion: "3.2.0",
            macAddress: "AA:BB:CC:DD:EE:FF");

        // Act
        var device = new DaqifiStreamingDevice(deviceInfo);

        // Assert
        Assert.AreEqual("Nq1", device.Name);
        Assert.AreEqual("10.0.0.42", device.IpAddress);
        Assert.AreEqual(9760, device.Port);
        Assert.AreEqual("SN-001", device.DeviceSerialNo);
        Assert.AreEqual("3.2.0", device.DeviceVersion);
        Assert.AreEqual("AA:BB:CC:DD:EE:FF", device.MacAddress);
        Assert.IsTrue(device.IsPowerOn);
        Assert.IsFalse(device.IsStreaming);
    }

    [TestMethod]
    public void Constructor_NullDeviceInfo_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new DaqifiStreamingDevice(null!));
    }

    [TestMethod]
    public void Constructor_MissingIpAddress_ShouldThrowArgumentException()
    {
        // Arrange
        var deviceInfo = new CoreDeviceInfo
        {
            Name = "Test Device",
            IPAddress = null,
            Port = 9760,
            SerialNumber = "12345",
            ConnectionType = CoreConnectionType.WiFi
        };

        // Act & Assert
        var ex = Assert.ThrowsExactly<ArgumentException>(() =>
            new DaqifiStreamingDevice(deviceInfo));
        Assert.IsTrue(ex.Message.Contains("IP address"), $"Expected IP address error, got: {ex.Message}");
    }

    [TestMethod]
    public void Constructor_MissingPort_ShouldThrowArgumentException()
    {
        // Arrange
        var deviceInfo = new CoreDeviceInfo
        {
            Name = "Test Device",
            IPAddress = IPAddress.Parse("192.168.1.100"),
            Port = null,
            SerialNumber = "12345",
            ConnectionType = CoreConnectionType.WiFi
        };

        // Act & Assert
        var ex = Assert.ThrowsExactly<ArgumentException>(() =>
            new DaqifiStreamingDevice(deviceInfo));
        Assert.IsTrue(ex.Message.Contains("TCP port"), $"Expected TCP port error, got: {ex.Message}");
    }

    #endregion

    #region Property Tests

    [TestMethod]
    public void ConnectionType_ShouldBeWifi()
    {
        // Arrange
        var device = new DaqifiStreamingDevice(CreateTestDeviceInfo());

        // Act & Assert
        Assert.AreEqual(ConnectionType.Wifi, device.ConnectionType);
    }

    [TestMethod]
    public void IsConnected_WhenNotConnected_ShouldReturnFalse()
    {
        // Arrange
        var device = new DaqifiStreamingDevice(CreateTestDeviceInfo());

        // Act & Assert
        Assert.IsFalse(device.IsConnected);
    }

    [TestMethod]
    public void DisplayIdentifier_ShouldReturnIpAddress()
    {
        // Arrange
        var device = new DaqifiStreamingDevice(
            CreateTestDeviceInfo(ipAddress: "10.0.0.99"));

        // Act & Assert
        Assert.AreEqual("10.0.0.99", device.DisplayIdentifier);
    }

    #endregion

    #region Write Tests

    [TestMethod]
    public void Write_ShouldThrowNotSupportedException()
    {
        // Arrange
        var device = new DaqifiStreamingDevice(CreateTestDeviceInfo());

        // Act & Assert
        Assert.ThrowsExactly<NotSupportedException>(() =>
            device.Write("SYSTem:STATus?"));
    }

    #endregion

    #region Disconnect Tests

    [TestMethod]
    public void Disconnect_WhenNotConnected_ShouldReturnTrue()
    {
        // Arrange
        var device = new DaqifiStreamingDevice(CreateTestDeviceInfo());

        // Act
        var result = device.Disconnect();

        // Assert
        Assert.IsTrue(result, "Disconnecting an unconnected device should succeed gracefully");
    }

    [TestMethod]
    public void Disconnect_ShouldClearDataChannels()
    {
        // Arrange
        var device = new DaqifiStreamingDevice(CreateTestDeviceInfo());

        // Act
        device.Disconnect();

        // Assert
        Assert.AreEqual(0, device.DataChannels.Count);
    }

    #endregion

    #region Equality Tests

    [TestMethod]
    public void Equals_SameProperties_ShouldReturnTrue()
    {
        // Arrange
        var a = new DaqifiStreamingDevice(CreateTestDeviceInfo());
        var b = new DaqifiStreamingDevice(CreateTestDeviceInfo());

        // Act & Assert
        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void Equals_DifferentIp_ShouldReturnFalse()
    {
        // Arrange
        var a = new DaqifiStreamingDevice(CreateTestDeviceInfo(ipAddress: "10.0.0.1"));
        var b = new DaqifiStreamingDevice(CreateTestDeviceInfo(ipAddress: "10.0.0.2"));

        // Act & Assert
        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void Equals_DifferentName_ShouldReturnFalse()
    {
        // Arrange
        var a = new DaqifiStreamingDevice(CreateTestDeviceInfo(name: "Device A"));
        var b = new DaqifiStreamingDevice(CreateTestDeviceInfo(name: "Device B"));

        // Act & Assert
        Assert.AreNotEqual(a, b);
    }

    #endregion

    #region ToString Tests

    [TestMethod]
    public void ToString_ShouldReturnName()
    {
        // Arrange
        var device = new DaqifiStreamingDevice(CreateTestDeviceInfo(name: "Nq1"));

        // Act & Assert
        Assert.AreEqual("Nq1", device.ToString());
    }

    #endregion
}
