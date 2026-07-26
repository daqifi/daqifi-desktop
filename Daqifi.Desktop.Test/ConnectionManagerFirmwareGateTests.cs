using Daqifi.Desktop.Device;
using Moq;

namespace Daqifi.Desktop.Test;

/// <summary>
/// Verifies the app-global firmware-update gate on <see cref="ConnectionManager"/> (issue #738): while
/// a firmware update is in progress, a user/discovery-initiated USB connect must be refused (the device
/// re-enumerates its COM port during the flash and Core reconnects it itself), and the
/// in-progress-state transitions must raise <see cref="ConnectionManager.FirmwareUpdateInProgressChanged"/>
/// so an open connection dialog can pause its discovery.
/// </summary>
[TestClass]
public class ConnectionManagerFirmwareGateTests
{
    [TestCleanup]
    public void TestCleanup()
    {
        // ConnectionManager is a process-wide singleton; leave the gate and device list clear for
        // other tests.
        ConnectionManager.Instance.DeviceBeingUpdated = null;
        ConnectionManager.Instance.ConnectedDevices.Clear();
    }

    [TestMethod]
    public void IsFirmwareUpdateInProgress_TracksDeviceBeingUpdated()
    {
        ConnectionManager.Instance.DeviceBeingUpdated = null;
        Assert.IsFalse(ConnectionManager.Instance.IsFirmwareUpdateInProgress);

        ConnectionManager.Instance.DeviceBeingUpdated = CreateDevice(ConnectionType.Usb).Object;
        Assert.IsTrue(ConnectionManager.Instance.IsFirmwareUpdateInProgress);

        ConnectionManager.Instance.DeviceBeingUpdated = null;
        Assert.IsFalse(ConnectionManager.Instance.IsFirmwareUpdateInProgress);
    }

    [TestMethod]
    public void SettingDeviceBeingUpdated_RaisesEventOnlyOnInProgressTransitions()
    {
        ConnectionManager.Instance.DeviceBeingUpdated = null;

        var raised = 0;
        void Handler(object? s, EventArgs e) => raised++;
        ConnectionManager.Instance.FirmwareUpdateInProgressChanged += Handler;
        try
        {
            // null -> device: flip to in-progress (raise)
            ConnectionManager.Instance.DeviceBeingUpdated = CreateDevice(ConnectionType.Usb).Object;
            Assert.AreEqual(1, raised);

            // device -> null: flip to idle (raise)
            ConnectionManager.Instance.DeviceBeingUpdated = null;
            Assert.AreEqual(2, raised);

            // null -> null: no change, no raise
            ConnectionManager.Instance.DeviceBeingUpdated = null;
            Assert.AreEqual(2, raised);
        }
        finally
        {
            ConnectionManager.Instance.FirmwareUpdateInProgressChanged -= Handler;
        }
    }

    [TestMethod]
    public async Task Connect_RefusesUsbDevice_WhileFirmwareUpdateInProgress()
    {
        ConnectionManager.Instance.DeviceBeingUpdated = CreateDevice(ConnectionType.Usb).Object;

        var incoming = CreateDevice(ConnectionType.Usb);

        await ConnectionManager.Instance.Connect(incoming.Object);

        Assert.AreEqual(DAQiFiConnectionStatus.Error, ConnectionManager.Instance.ConnectionStatus);
        // The gate short-circuits before ever opening the port.
        incoming.Verify(d => d.Connect(), Times.Never);
    }

    [TestMethod]
    public async Task Connect_DoesNotGateWifiDevice_WhileFirmwareUpdateInProgress()
    {
        ConnectionManager.Instance.DeviceBeingUpdated = CreateDevice(ConnectionType.Usb).Object;

        // A WiFi connect targets a different device path and cannot steal the flashing device's COM
        // port, so the gate must not block it — Connect() should still be attempted.
        var incoming = CreateDevice(ConnectionType.Wifi);
        incoming.Setup(d => d.Connect()).Returns(false); // stop the flow right after the gate

        await ConnectionManager.Instance.Connect(incoming.Object);

        incoming.Verify(d => d.Connect(), Times.Once);
    }

    [TestMethod]
    public void OnDeviceConnectionLost_DoesNotTearDownDeviceBeingUpdated()
    {
        // The dominant #738 cause: when the flashing device reboots mid-update, Core raises a
        // transport-lost event; the desktop must NOT tear down (Disconnect/dispose) that Core device,
        // because Core reconnects the SAME instance at JumpingToApp. Disposing it strands the update in
        // a JumpingToApp timeout even though the flash succeeded. Guarded by IsDeviceBeingUpdated.
        var device = CreateDevice(ConnectionType.Usb);
        ConnectionManager.Instance.DeviceBeingUpdated = device.Object;

        InvokePrivate("OnDeviceConnectionLost", device.Object,
            new ConnectionLostEventArgs("reboot"));

        // The device must NOT have been disconnected — Disconnect() calls device.Disconnect().
        device.Verify(d => d.Disconnect(), Times.Never);
    }

    [TestMethod]
    public void OnDeviceConnectionLost_TearsDownOtherDevices()
    {
        // Control: a device that is NOT being updated still tears down normally on a lost connection.
        var updating = CreateDevice(ConnectionType.Usb);
        updating.SetupGet(d => d.Name).Returns("Updating");
        ConnectionManager.Instance.DeviceBeingUpdated = updating.Object;

        var other = CreateDevice(ConnectionType.Usb);
        other.SetupGet(d => d.Name).Returns("Other");
        other.SetupGet(d => d.DataChannels).Returns(new List<Daqifi.Desktop.Channel.IChannel>());
        // Put it in ConnectedDevices so the teardown path proceeds past the Contains guard.
        ConnectionManager.Instance.ConnectedDevices.Add(other.Object);

        InvokePrivate("OnDeviceConnectionLost", other.Object,
            new ConnectionLostEventArgs("cable pulled"));

        other.Verify(d => d.Disconnect(), Times.Once);
    }

    [TestMethod]
    public void ClearingDeviceBeingUpdated_TearsDownDeviceThatFailedToReconnect()
    {
        // Follow-up to the teardown-skip: when the update ends, a device Core failed to reconnect (e.g.
        // a JumpingToApp timeout) is still in ConnectedDevices with a dead transport. Clearing
        // DeviceBeingUpdated must reconcile it — otherwise it shows as connected forever (Qodo #738).
        var device = CreateDevice(ConnectionType.Usb);
        device.SetupGet(d => d.IsConnected).Returns(false);
        device.SetupGet(d => d.DataChannels).Returns(new List<Daqifi.Desktop.Channel.IChannel>());
        ConnectionManager.Instance.ConnectedDevices.Add(device.Object);
        ConnectionManager.Instance.DeviceBeingUpdated = device.Object;

        ConnectionManager.Instance.DeviceBeingUpdated = null;

        device.Verify(d => d.Disconnect(), Times.Once);
        Assert.IsFalse(ConnectionManager.Instance.ConnectedDevices.Contains(device.Object));
    }

    [TestMethod]
    public void ClearingDeviceBeingUpdated_LeavesReconnectedDeviceConnected()
    {
        // On a successful flash Core reconnected the device (IsConnected == true); the reconciliation
        // must be a no-op so it stays connected.
        var device = CreateDevice(ConnectionType.Usb);
        device.SetupGet(d => d.IsConnected).Returns(true);
        ConnectionManager.Instance.ConnectedDevices.Add(device.Object);
        ConnectionManager.Instance.DeviceBeingUpdated = device.Object;

        ConnectionManager.Instance.DeviceBeingUpdated = null;

        device.Verify(d => d.Disconnect(), Times.Never);
        Assert.IsTrue(ConnectionManager.Instance.ConnectedDevices.Contains(device.Object));
    }

    private static void InvokePrivate(string methodName, params object[] args)
    {
        var method = typeof(ConnectionManager).GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(method, $"{methodName} not found.");
        method.Invoke(ConnectionManager.Instance, args);
    }

    private static Mock<IStreamingDevice> CreateDevice(ConnectionType connectionType)
    {
        var device = new Mock<IStreamingDevice>();
        device.SetupGet(d => d.ConnectionType).Returns(connectionType);
        device.SetupGet(d => d.Name).Returns($"Device-{connectionType}");
        device.SetupGet(d => d.DeviceSerialNo).Returns(string.Empty);
        return device;
    }
}
