using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Device;
using Daqifi.Core.Communication.Messages;
using Daqifi.Core.Device;
using CoreStreamingDevice = Daqifi.Core.Device.DaqifiStreamingDevice;

namespace Daqifi.Desktop.Test.Device;

/// <summary>
/// Tests for the shared Core connect/wire/cleanup template in
/// <see cref="AbstractStreamingDevice"/> (issue #591).
/// </summary>
[TestClass]
public class CoreConnectionTemplateTests
{
    private static readonly string[] ExpectedFullConnectHookCalls =
        ["CleanupConnection", "CreateCoreDevice", "InitializeAsync", "OnCoreDeviceInitialized"];

    private static readonly string[] ExpectedAbortedConnectHookCalls =
        ["CleanupConnection", "CreateCoreDevice"];

    [TestMethod]
    public void Connect_RunsTemplateStepsInOrder()
    {
        // Arrange
        var device = new TemplateTestDevice();

        // Act
        var connected = device.Connect();

        // Assert
        Assert.IsTrue(connected, "Connect should succeed when the Core device is created.");
        Assert.IsNotNull(device.ExposedCoreDevice, "The created Core device should be retained.");
        Assert.AreSame(device.CreatedCoreDevice, device.ExposedCoreDevice);
        CollectionAssert.AreEqual(
            ExpectedFullConnectHookCalls,
            device.HookCalls,
            "The template must clean up, create, initialize, then run the post-initialize hook.");
        Assert.AreEqual(0, device.LoggedConnectFailures.Count, "No failure should be logged on success.");
    }

    [TestMethod]
    public void Connect_WiresChannelsPopulatedEventToCoreSync()
    {
        // Arrange
        var device = new TemplateTestDevice();
        device.Connect();

        // Act — raise the real Core event by populating channels from a status message
        var statusMessage = BuildStatusMessage();
        device.CreatedCoreDevice!.Metadata.UpdateFromProtobuf(statusMessage);
        device.CreatedCoreDevice.PopulateChannelsFromStatus(statusMessage);

        // Assert
        Assert.AreEqual(2, device.DataChannels.Count, "Channel sync should run via the wired event.");
        Assert.AreEqual(1, device.DataChannels.OfType<AnalogChannel>().Count());
        Assert.AreEqual(1, device.DataChannels.OfType<DigitalChannel>().Count());
        Assert.AreEqual("1.2.3", device.DeviceVersion, "Metadata should hydrate via the wired event.");
    }

    /// <summary>
    /// Metadata hydration hands the field list to Core's <c>DeviceMetadata.CopyFrom</c> rather than
    /// enumerating fields here (daqifi-core#305). The hand-rolled copy it replaced listed 13 fields
    /// and silently dropped any Core added later — <c>FriendlyName</c> and <c>Health</c> were
    /// already being dropped when it was removed.
    /// </summary>
    [TestMethod]
    public void ChannelsPopulated_HydratesEveryMetadataField_NotJustAHandPickedSubset()
    {
        // Arrange
        var device = new TemplateTestDevice();
        device.Connect();

        var statusMessage = BuildStatusMessage();
        device.CreatedCoreDevice!.Metadata.UpdateFromProtobuf(statusMessage);

        // A field the replaced hand-rolled copy did not carry across.
        device.CreatedCoreDevice.Metadata.FriendlyName = "Bench Rig 3";

        // Act
        device.CreatedCoreDevice.PopulateChannelsFromStatus(statusMessage);

        // Assert
        Assert.AreEqual("Bench Rig 3", device.Metadata.FriendlyName,
            "Delegating to CopyFrom is what stops a Core metadata field from being silently dropped here.");
        Assert.AreEqual("Nq1", device.Metadata.PartNumber);
        Assert.AreEqual("12345", device.Metadata.SerialNumber);
    }

    /// <summary>
    /// The wrapper's capabilities must stay an independent copy: Core rebuilds
    /// <c>Metadata.Capabilities</c> on every status message that carries a part number, so sharing
    /// the instance would let a Core-side rebuild mutate what the UI is bound to.
    /// </summary>
    [TestMethod]
    public void ChannelsPopulated_CopiesCapabilitiesRatherThanSharingCoresInstance()
    {
        // Arrange
        var device = new TemplateTestDevice();
        device.Connect();

        var statusMessage = BuildStatusMessage();
        device.CreatedCoreDevice!.Metadata.UpdateFromProtobuf(statusMessage);

        // Act
        device.CreatedCoreDevice.PopulateChannelsFromStatus(statusMessage);

        // Assert
        Assert.IsNotNull(device.Capabilities);
        Assert.AreNotSame(device.CreatedCoreDevice.Metadata.Capabilities, device.Capabilities,
            "Capabilities must be deep-copied, not aliased to Core's live instance.");
        Assert.AreEqual(
            device.CreatedCoreDevice.Metadata.Capabilities.AnalogInputChannels,
            device.Capabilities.AnalogInputChannels,
            "The copy must still carry Core's values.");
    }

    [TestMethod]
    public void Connect_WhenCreateCoreDeviceReturnsNull_ReturnsFalseWithoutFailureLogging()
    {
        // Arrange — a factory returning null means the failure was already logged
        var device = new TemplateTestDevice(returnNullCoreDevice: true);

        // Act
        var connected = device.Connect();

        // Assert
        Assert.IsFalse(connected);
        Assert.IsNull(device.ExposedCoreDevice);
        Assert.AreEqual(0, device.LoggedConnectFailures.Count,
            "A null factory result must not be double-logged as a connect failure.");
        CollectionAssert.AreEqual(
            ExpectedAbortedConnectHookCalls,
            device.HookCalls,
            "Initialization must not run when no Core device was created.");
    }

    [TestMethod]
    public void Connect_WhenCreateCoreDeviceThrows_LogsFailureAndCleansUp()
    {
        // Arrange
        var failure = new InvalidOperationException("Transport exploded.");
        var device = new TemplateTestDevice(createException: failure);

        // Act
        var connected = device.Connect();

        // Assert
        Assert.IsFalse(connected);
        Assert.AreEqual(1, device.LoggedConnectFailures.Count);
        Assert.AreSame(failure, device.LoggedConnectFailures[0],
            "The original exception must reach the classification hook.");
        Assert.AreEqual("CleanupConnection", device.HookCalls.Last(),
            "Failure must clean up the connection.");
        Assert.IsNull(device.ExposedCoreDevice);
    }

    [TestMethod]
    public void Connect_WhenPostInitializeThrows_CleansUpCoreDeviceAndUnsubscribesEvents()
    {
        // Arrange — serial's initial-status wait throwing is the real-world case
        var failure = new TimeoutException("Device did not report status.");
        var device = new TemplateTestDevice(postInitializeException: failure);

        // Act
        var connected = device.Connect();

        // Assert
        Assert.IsFalse(connected);
        Assert.AreSame(failure, device.LoggedConnectFailures.Single());
        Assert.AreEqual("CleanupConnection", device.HookCalls.Last(),
            "Failure must clean up the connection.");
        Assert.IsNull(device.ExposedCoreDevice, "Cleanup must drop the Core device on failure.");

        // The Core device outlives the failed attempt; its events must be unsubscribed.
        var statusMessage = BuildStatusMessage();
        device.CreatedCoreDevice!.Metadata.UpdateFromProtobuf(statusMessage);
        device.CreatedCoreDevice.PopulateChannelsFromStatus(statusMessage);
        Assert.AreEqual(0, device.ChannelsPopulatedHandlerCalls,
            "ChannelsPopulated must be unsubscribed after a failed connect.");
        Assert.AreEqual(0, device.DataChannels.Count);
    }

    [TestMethod]
    public void Disconnect_UnsubscribesClearsChannelsAndDropsCoreDevice()
    {
        // Arrange — connected device with synced channels
        var device = new TemplateTestDevice();
        device.Connect();
        var coreDevice = device.CreatedCoreDevice!;
        var statusMessage = BuildStatusMessage();
        coreDevice.Metadata.UpdateFromProtobuf(statusMessage);
        coreDevice.PopulateChannelsFromStatus(statusMessage);
        Assert.AreEqual(2, device.DataChannels.Count, "Precondition: channels synced.");

        // Act
        var disconnected = device.Disconnect();

        // Assert
        Assert.IsTrue(disconnected);
        Assert.AreEqual(0, device.DataChannels.Count, "Channels must clear to prevent ghosts (issue #29).");
        Assert.IsNull(device.ExposedCoreDevice);
        Assert.AreEqual("CleanupConnection", device.HookCalls.Last(),
            "Disconnect must run the shared cleanup.");

        // A late Core event after disconnect must not repopulate the channel list.
        var handlerCallsBefore = device.ChannelsPopulatedHandlerCalls;
        coreDevice.PopulateChannelsFromStatus(statusMessage);
        Assert.AreEqual(handlerCallsBefore, device.ChannelsPopulatedHandlerCalls,
            "ChannelsPopulated must be unsubscribed on disconnect.");
        Assert.AreEqual(0, device.DataChannels.Count);
    }

    [TestMethod]
    public void Disconnect_UnsubscribesCoresClassifiedMessageEvents()
    {
        // Arrange — a connected device receiving Core's classified frames. Core classifies each
        // inbound frame once and raises StatusMessageReceived/StreamMessageReceived (daqifi-core#308);
        // the wrapper subscribes to both in SubscribeCoreDeviceEvents.
        var device = new TemplateTestDevice();
        device.Connect();
        var coreDevice = device.CreatedCoreDevice!;
        coreDevice.SimulateStatusMessage(BuildStatusMessage(friendlyDeviceName: "Bench NQ1"));
        Assert.AreEqual("Bench NQ1", device.FriendlyName,
            "Precondition: Core's classified status event must reach the wrapper while connected.");

        // Act
        device.Disconnect();

        // Assert — the Core device outlives the wrapper's connection (Disconnect only drops the
        // wrapper's reference), so every classified subscription must have a matching removal or a
        // late frame would keep mutating the disconnected device's bound state. Covers both events
        // added when the desktop stopped running its own ProtobufProtocolHandler pass.
        coreDevice.SimulateStatusMessage(BuildStatusMessage(friendlyDeviceName: "A different device"));
        Assert.AreEqual("Bench NQ1", device.FriendlyName,
            "StatusMessageReceived must be unsubscribed on disconnect.");

        coreDevice.SimulateStreamMessage(new DaqifiOutMessage
        {
            MsgTimeStamp = 1000,
            DeviceSn = 12345,
            FriendlyDeviceName = "A different device",
            AnalogInDataFloat = { 1.25f }
        });
        Assert.AreEqual("Bench NQ1", device.FriendlyName,
            "StreamMessageReceived must be unsubscribed on disconnect.");
    }

    [TestMethod]
    public void Connect_WhenNotOverridden_ReturnsFalse()
    {
        // Arrange — a device that does not provide a Core device factory
        var device = new HookFreeTestDevice();

        // Act & Assert
        Assert.IsFalse(device.Connect(),
            "The default template must fail safely when CreateCoreDevice is not overridden.");
    }

    /// <summary>
    /// A device that boots with channels already enabled must not hand the app an active set it
    /// never chose (issue #811).
    /// </summary>
    /// <remarks>
    /// Firmware persists its enabled set, and since Core 1.4.0 (daqifi-core#409) that set is resynced
    /// onto <c>IsEnabled</c> from every status message. <see cref="IChannel.IsActive"/> reads straight
    /// through, so without this every channel tile rendered active while nothing was subscribed to
    /// <c>LoggingManager</c> — and <c>ChannelsPaneViewModel.SelectAll</c>, which skips tiles already
    /// reporting active, became a no-op that left Start Logging permanently disabled.
    /// </remarks>
    [TestMethod]
    public void Connect_ClearsChannelsTheDeviceReportsAsAlreadyEnabled()
    {
        // Arrange
        var device = new TemplateTestDevice(deviceReportsChannelsEnabled: true);

        // Act
        var connected = device.Connect();

        // Assert
        Assert.IsTrue(connected, "Adopting the channel set must not fail the connection.");
        Assert.IsTrue(
            device.CreatedCoreDevice!.Channels!.Count > 0,
            "Guard: the fixture must actually have populated channels, or this asserts nothing.");
        Assert.IsTrue(
            device.CreatedCoreDevice.Channels!.All(channel => !channel.IsEnabled),
            "The app starts with an empty active set, so a device reporting channels already " +
            "enabled must be reconciled toward the app rather than the other way around.");
    }

    [TestMethod]
    public void Connect_SucceedsWhenTheDeviceMerelyRefusesToClearChannels()
    {
        // Arrange — adoption is best-effort. A device that declines the disable is survivable: the
        // user can still pick channels by hand, so the connection must stand.
        var device = new TemplateTestDevice(
            deviceReportsChannelsEnabled: true,
            disableAllChannelsException: new InvalidOperationException("Device returned a SCPI error."));

        // Act
        var connected = device.Connect();

        // Assert
        Assert.IsTrue(connected, "A refused disable must not fail an otherwise good connection.");
        Assert.AreEqual(0, device.LoggedConnectFailures.Count, "A refusal is not a connect failure.");
    }

    /// <summary>
    /// A dropped transport during adoption must fail the connection, not be swallowed as
    /// best-effort.
    /// </summary>
    /// <remarks>
    /// Swallowing it would let <c>Connect</c> return <c>true</c> for a device that is already gone,
    /// so <c>ConnectionManager</c> would add it to <c>ConnectedDevices</c> having skipped both
    /// <c>LogConnectFailure</c> and <c>CleanupConnection</c>. Same hazard as issue #619, where
    /// delegating to Core turned disconnected no-ops into throws.
    /// </remarks>
    [TestMethod]
    public void Connect_FailsWhenTheTransportDroppedWhileClearingChannels()
    {
        // Arrange
        var device = new TemplateTestDevice(
            deviceReportsChannelsEnabled: true,
            disableAllChannelsException: new InvalidOperationException("Transport is not connected."));

        // Act
        var connected = device.Connect();

        // Assert
        Assert.IsFalse(connected, "A dropped transport must fail the connection.");
        Assert.AreEqual(1, device.LoggedConnectFailures.Count,
            "The failure must reach the connect template's own handling, not be swallowed.");
        Assert.IsNull(device.ExposedCoreDevice, "CleanupConnection must have torn the Core device down.");
    }

    [TestMethod]
    public void Connect_LeavesChannelsAloneWhenTheDeviceReportsNoneEnabled()
    {
        // Arrange — the ordinary case. Regression guard in the other direction: the adopt step must
        // not fire (and must not send a disable) when there is nothing to reconcile.
        var device = new TemplateTestDevice();

        // Act
        var connected = device.Connect();

        // Assert
        Assert.IsTrue(connected);
        CollectionAssert.AreEqual(
            ExpectedFullConnectHookCalls,
            device.HookCalls,
            "Adopting an already-empty set must not disturb the template's hook sequence.");
    }

    private static DaqifiOutMessage BuildStatusMessage(string? friendlyDeviceName = null)
    {
        return new DaqifiOutMessage
        {
            DevicePn = "Nq1",
            DeviceSn = 12345,
            DeviceFwRev = "1.2.3",
            FriendlyDeviceName = friendlyDeviceName ?? string.Empty,
            AnalogInPortNum = 1,
            AnalogInRes = 4095,
            DigitalPortNum = 1,
            AnalogInCalM = { 1.0f },
            AnalogInCalB = { 0.0f },
            AnalogInIntScaleM = { 1.0f },
            AnalogInPortRange = { 5.0f }
        };
    }

    /// <summary>
    /// Streaming device exercising the shared connect template with recording hooks.
    /// </summary>
    private sealed class TemplateTestDevice(
        bool returnNullCoreDevice = false,
        Exception? createException = null,
        Exception? postInitializeException = null,
        bool deviceReportsChannelsEnabled = false,
        Exception? disableAllChannelsException = null) : AbstractStreamingDevice
    {
        public List<string> HookCalls { get; } = [];
        public List<Exception> LoggedConnectFailures { get; } = [];
        public TemplateCoreDevice? CreatedCoreDevice { get; private set; }
        public CoreStreamingDevice? ExposedCoreDevice => CoreDevice;
        public int ChannelsPopulatedHandlerCalls { get; private set; }

        public override ConnectionType ConnectionType => ConnectionType.Usb;

        public override bool Write(string command) => true;

        protected override void SendMessage(IOutboundMessage<string> message)
        {
        }

        protected override CoreStreamingDevice? CreateCoreDevice()
        {
            HookCalls.Add("CreateCoreDevice");

            if (createException != null)
            {
                throw createException;
            }

            if (returnNullCoreDevice)
            {
                return null;
            }

            CreatedCoreDevice = new TemplateCoreDevice(() =>
            {
                HookCalls.Add("InitializeAsync");

                if (!deviceReportsChannelsEnabled)
                {
                    return;
                }

                // Models firmware that persists its enabled set across power cycles: a bench Nq1 on
                // 3.7.2 reports analog_in_port_enabled = FFFF, and Core 1.4.0 resyncs IsEnabled from
                // it (daqifi-core#409). Set directly rather than through the protobuf mask so the
                // test does not depend on Core's bit layout — what matters is that channels are
                // already enabled by the time the connect template's adopt step runs.
                CreatedCoreDevice!.PopulateChannelsFromStatus(BuildStatusMessage());
                foreach (var channel in CreatedCoreDevice.Channels!)
                {
                    channel.IsEnabled = true;
                }

                // Armed last so it fails the adopt step specifically, not initialization.
                CreatedCoreDevice.SendException = disableAllChannelsException;
            });
            CreatedCoreDevice.Connect();
            return CreatedCoreDevice;
        }

        protected override void OnCoreDeviceInitialized()
        {
            HookCalls.Add("OnCoreDeviceInitialized");

            if (postInitializeException != null)
            {
                throw postInitializeException;
            }
        }

        protected override void LogConnectFailure(Exception ex)
        {
            LoggedConnectFailures.Add(ex);
        }

        protected override void CleanupConnection()
        {
            HookCalls.Add("CleanupConnection");
            base.CleanupConnection();
        }

        protected override void OnCoreChannelsPopulated(object? sender, ChannelsPopulatedEventArgs e)
        {
            ChannelsPopulatedHandlerCalls++;
            base.OnCoreChannelsPopulated(sender, e);
        }
    }

    /// <summary>
    /// Device relying entirely on the base template defaults (no CreateCoreDevice override).
    /// </summary>
    private sealed class HookFreeTestDevice : AbstractStreamingDevice
    {
        public override ConnectionType ConnectionType => ConnectionType.Usb;

        public override bool Write(string command) => true;

        protected override void SendMessage(IOutboundMessage<string> message)
        {
        }
    }

    /// <summary>
    /// Transportless Core device with a stubbed initialization sequence.
    /// </summary>
    private sealed class TemplateCoreDevice(Action onInitialize) : CoreStreamingDevice("TemplateCore")
    {
        /// <summary>
        /// When set, the next outbound command throws this instead of being swallowed.
        /// </summary>
        /// <remarks>
        /// Core's <c>DisableAllChannels</c> is not virtual, so the connect template's channel-set
        /// adoption is failed through the transport it ultimately writes to. Armed only after
        /// initialization, so it targets the adopt step rather than anything Core sends earlier.
        /// </remarks>
        public Exception? SendException { get; set; }

        public override Task InitializeAsync(
            TimeSpan? channelPopulationTimeout = null,
            CancellationToken cancellationToken = default)
        {
            onInitialize();
            return Task.CompletedTask;
        }

        public override void Send<T>(IOutboundMessage<T> message)
        {
            if (SendException != null)
            {
                throw SendException;
            }
        }

        /// <summary>
        /// Runs Core's status-message path for <paramref name="message"/>, which raises the
        /// classified <c>StatusMessageReceived</c> event the wrapper subscribes to.
        /// </summary>
        /// <param name="message">The frame Core has classified as a status message.</param>
        public void SimulateStatusMessage(DaqifiOutMessage message) => OnStatusMessageReceived(message);

        /// <summary>
        /// Runs Core's stream-message path for <paramref name="message"/>, which raises the
        /// classified <c>StreamMessageReceived</c> event the wrapper subscribes to.
        /// </summary>
        /// <param name="message">The frame Core has classified as a streaming frame.</param>
        public void SimulateStreamMessage(DaqifiOutMessage message) => OnStreamMessageReceived(message);
    }
}
