using Daqifi.Desktop.Device;
using Moq;

namespace Daqifi.Desktop.Test;

/// <summary>
/// Covers the resource handling of <see cref="ConnectionManager"/>'s post-connect duplicate check —
/// the branch that runs when a device only reveals its serial number after the port is already open
/// (the WiFi/USB double-connect case). The port was opened, so rejecting the device has to release it:
/// a <c>SerialStreamingDevice</c> that is disconnected but never disposed keeps its COM handle for the
/// process lifetime and blocks every later reconnect to that port.
/// </summary>
[TestClass]
public class ConnectionManagerDuplicateDeviceTests
{
    private const string SHARED_SERIAL = "SN-DUPLICATE";

    [TestCleanup]
    public void TestCleanup()
    {
        // ConnectionManager is a process-wide singleton; leave it clean for other tests.
        ConnectionManager.Instance.DeviceBeingUpdated = null;
        ConnectionManager.Instance.ConnectedDevices.Clear();
        ConnectionManager.Instance.DuplicateDeviceHandler = null;
    }

    [TestMethod]
    public async Task Connect_DisconnectsAndDisposesDevice_WhenItTurnsOutToBeADuplicateAfterConnecting()
    {
        ConnectionManager.Instance.ConnectedDevices.Add(CreateDevice(SHARED_SERIAL).Object);

        // The incoming device has no serial number until Connect() has opened the port, so the
        // pre-connect duplicate check cannot see the collision and the port really does get opened.
        var incoming = CreateDeviceRevealingSerialOnConnect(SHARED_SERIAL);
        var disposable = incoming.As<IDisposable>();

        await ConnectionManager.Instance.Connect(incoming.Object);

        incoming.Verify(d => d.Connect(), Times.Once);
        incoming.Verify(d => d.Disconnect(), Times.Once);
        disposable.Verify(d => d.Dispose(), Times.Once);
    }

    [TestMethod]
    public async Task Connect_RejectsPostConnectDuplicate_WithoutAddingItToConnectedDevices()
    {
        var existing = CreateDevice(SHARED_SERIAL).Object;
        ConnectionManager.Instance.ConnectedDevices.Add(existing);

        var incoming = CreateDeviceRevealingSerialOnConnect(SHARED_SERIAL);
        incoming.As<IDisposable>();

        await ConnectionManager.Instance.Connect(incoming.Object);

        Assert.AreEqual(DAQiFiConnectionStatus.AlreadyConnected, ConnectionManager.Instance.ConnectionStatus);
        CollectionAssert.AreEqual(
            new List<IStreamingDevice> { existing },
            ConnectionManager.Instance.ConnectedDevices.ToList());
    }

    [TestMethod]
    public async Task Connect_DoesNotDisposeDevice_WhenItIsNotADuplicate()
    {
        // Control: the disposal above must be scoped to the rejection path, never to a device that
        // is about to be handed to the rest of the app.
        var incoming = CreateDeviceRevealingSerialOnConnect("SN-UNIQUE");
        var disposable = incoming.As<IDisposable>();

        await ConnectionManager.Instance.Connect(incoming.Object);

        Assert.AreEqual(DAQiFiConnectionStatus.Connected, ConnectionManager.Instance.ConnectionStatus);
        Assert.IsTrue(ConnectionManager.Instance.ConnectedDevices.Contains(incoming.Object));
        incoming.Verify(d => d.Disconnect(), Times.Never);
        disposable.Verify(d => d.Dispose(), Times.Never);
    }

    private static Mock<IStreamingDevice> CreateDevice(string serialNo)
    {
        var device = new Mock<IStreamingDevice>();
        device.SetupGet(d => d.ConnectionType).Returns(ConnectionType.Wifi);
        device.SetupGet(d => d.Name).Returns($"Device-{serialNo}");
        device.SetupGet(d => d.DeviceSerialNo).Returns(serialNo);
        device.SetupGet(d => d.MacAddress).Returns(string.Empty);
        device.SetupGet(d => d.DataChannels).Returns(new List<Daqifi.Desktop.Channel.IChannel>());
        return device;
    }

    /// <summary>
    /// Builds a USB device whose serial number is blank until <c>Connect()</c> succeeds — the case
    /// the post-connect duplicate check exists for.
    /// </summary>
    private static Mock<IStreamingDevice> CreateDeviceRevealingSerialOnConnect(string serialNo)
    {
        var device = new Mock<IStreamingDevice>();
        var revealedSerial = string.Empty;

        device.SetupGet(d => d.ConnectionType).Returns(ConnectionType.Usb);
        device.SetupGet(d => d.Name).Returns("Incoming");
        device.SetupGet(d => d.MacAddress).Returns(string.Empty);
        device.SetupGet(d => d.DeviceSerialNo).Returns(() => revealedSerial);
        device.SetupGet(d => d.DataChannels).Returns(new List<Daqifi.Desktop.Channel.IChannel>());
        device.Setup(d => d.Connect()).Returns(() =>
        {
            revealedSerial = serialNo;
            return true;
        });

        return device;
    }
}
