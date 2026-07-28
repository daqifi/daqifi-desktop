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
        Exception? postInitializeException = null) : AbstractStreamingDevice
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

            CreatedCoreDevice = new TemplateCoreDevice(() => HookCalls.Add("InitializeAsync"));
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
        public override Task InitializeAsync(
            TimeSpan? channelPopulationTimeout = null,
            CancellationToken cancellationToken = default)
        {
            onInitialize();
            return Task.CompletedTask;
        }

        public override void Send<T>(IOutboundMessage<T> message)
        {
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
