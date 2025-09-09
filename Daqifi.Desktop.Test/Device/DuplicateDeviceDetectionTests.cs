using Daqifi.Desktop.Device;
using Daqifi.Desktop.DataModel.Device;
using Moq;

namespace Daqifi.Desktop.Test.Device;

[TestClass]
public class DuplicateDeviceDetectionTests
{
    #region Private Variables
    private ConnectionManager _connectionManager;
    private Mock<IStreamingDevice> _mockDevice1;
    private Mock<IStreamingDevice> _mockDevice2;
    #endregion

    #region Test Setup
    [TestInitialize]
    public void Setup()
    {
        _connectionManager = new ConnectionManager();
        _mockDevice1 = new Mock<IStreamingDevice>();
        _mockDevice2 = new Mock<IStreamingDevice>();
    }
    #endregion

    #region Duplicate Detection Tests
    [TestMethod]
    public async Task Connect_WithDuplicateSerialNumber_ShouldSetAlreadyConnectedStatus()
    {
        // Arrange
        const string serialNumber = "DAQ-12345";
        
        _mockDevice1.Setup(d => d.DeviceSerialNo).Returns(serialNumber);
        _mockDevice1.Setup(d => d.ConnectionType).Returns(ConnectionType.Usb);
        _mockDevice1.Setup(d => d.Connect()).Returns(true);
        
        _mockDevice2.Setup(d => d.DeviceSerialNo).Returns(serialNumber);
        _mockDevice2.Setup(d => d.ConnectionType).Returns(ConnectionType.WiFi);

        // Connect first device
        await _connectionManager.Connect(_mockDevice1.Object);

        // Act - try to connect duplicate device without handler
        await _connectionManager.Connect(_mockDevice2.Object);

        // Assert
        Assert.AreEqual(DAQifiConnectionStatus.AlreadyConnected, _connectionManager.ConnectionStatus);
        Assert.AreEqual(1, _connectionManager.ConnectedDevices.Count, "Should only have one device connected");
    }

    [TestMethod]
    public async Task Connect_WithDifferentSerialNumbers_ShouldConnectBothDevices()
    {
        // Arrange
        _mockDevice1.Setup(d => d.DeviceSerialNo).Returns("DAQ-12345");
        _mockDevice1.Setup(d => d.ConnectionType).Returns(ConnectionType.Usb);
        _mockDevice1.Setup(d => d.Connect()).Returns(true);
        
        _mockDevice2.Setup(d => d.DeviceSerialNo).Returns("DAQ-67890");
        _mockDevice2.Setup(d => d.ConnectionType).Returns(ConnectionType.WiFi);
        _mockDevice2.Setup(d => d.Connect()).Returns(true);

        // Act
        await _connectionManager.Connect(_mockDevice1.Object);
        await _connectionManager.Connect(_mockDevice2.Object);

        // Assert
        Assert.AreEqual(DAQifiConnectionStatus.Connected, _connectionManager.ConnectionStatus);
        Assert.AreEqual(2, _connectionManager.ConnectedDevices.Count, "Should have both devices connected");
    }

    [TestMethod]
    public async Task Connect_WithEmptySerialNumber_ShouldAllowConnection()
    {
        // Arrange
        _mockDevice1.Setup(d => d.DeviceSerialNo).Returns("DAQ-12345");
        _mockDevice1.Setup(d => d.ConnectionType).Returns(ConnectionType.Usb);
        _mockDevice1.Setup(d => d.Connect()).Returns(true);
        
        _mockDevice2.Setup(d => d.DeviceSerialNo).Returns("");
        _mockDevice2.Setup(d => d.ConnectionType).Returns(ConnectionType.WiFi);
        _mockDevice2.Setup(d => d.Connect()).Returns(true);

        // Act
        await _connectionManager.Connect(_mockDevice1.Object);
        await _connectionManager.Connect(_mockDevice2.Object);

        // Assert
        Assert.AreEqual(DAQifiConnectionStatus.Connected, _connectionManager.ConnectionStatus);
        Assert.AreEqual(2, _connectionManager.ConnectedDevices.Count, "Should allow device with no serial number");
    }

    [TestMethod]
    public async Task Connect_WithDuplicateHandler_ShouldCallHandler()
    {
        // Arrange
        const string serialNumber = "DAQ-12345";
        bool handlerCalled = false;
        
        _mockDevice1.Setup(d => d.DeviceSerialNo).Returns(serialNumber);
        _mockDevice1.Setup(d => d.ConnectionType).Returns(ConnectionType.Usb);
        _mockDevice1.Setup(d => d.Connect()).Returns(true);
        
        _mockDevice2.Setup(d => d.DeviceSerialNo).Returns(serialNumber);
        _mockDevice2.Setup(d => d.ConnectionType).Returns(ConnectionType.WiFi);

        _connectionManager.DuplicateDeviceHandler = (result) =>
        {
            handlerCalled = true;
            return DuplicateDeviceAction.KeepExisting;
        };

        // Connect first device
        await _connectionManager.Connect(_mockDevice1.Object);

        // Act
        await _connectionManager.Connect(_mockDevice2.Object);

        // Assert
        Assert.IsTrue(handlerCalled, "Duplicate device handler should be called");
        Assert.AreEqual(1, _connectionManager.ConnectedDevices.Count, "Should keep existing device");
    }
    #endregion
}