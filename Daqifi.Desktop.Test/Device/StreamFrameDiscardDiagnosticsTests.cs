using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
using Daqifi.Core.Communication.Messages;
using Daqifi.Core.Device;
using Moq;
using System.Reflection;
using BreadcrumbLevel = Daqifi.Desktop.Common.Loggers.BreadcrumbLevel;
using CoreStreamingDevice = Daqifi.Core.Device.DaqifiStreamingDevice;

namespace Daqifi.Desktop.Test.Device;

/// <summary>
/// Covers what replaced the desktop's own stream-start leftover-frame guard (issue #679). The
/// device latches the final frame of a stopped session in its transmit path and emits it as the
/// first frame of the next one (issue #573, daqifi-nyquist-firmware#533); the desktop used to
/// recognize and drop that frame itself, and since Core v1.4.0 Core does it — screening the raw
/// frame path as well — and reports each drop through
/// <see cref="CoreStreamingDevice.StreamFrameDiscarded"/> (daqifi-core#425/#428).
/// <para>
/// These tests drive Core's real <c>StreamFrameGate</c> end to end through the desktop wrapper
/// rather than mocking the event, because the deletion is only safe if the protection genuinely
/// survives it: the desktop no longer has any leftover-frame code of its own to test.
/// </para>
/// </summary>
[TestClass]
public class StreamFrameDiscardDiagnosticsTests : IDisposable
{
    // Core's detection window is 2.5 sample periods of the session's streaming frequency, so the
    // rate the session starts at decides what counts as a leftover. At 100 Hz against the 50 MHz
    // default tick rate that is 1,250,000 ticks (25 ms).
    private const int STREAMING_FREQUENCY_HZ = 100;
    private const uint SAMPLE_PERIOD_TICKS = 500_000;
    private const uint THIRTEEN_SECOND_GAP_TICKS = 650_000_000;

    private const string BREADCRUMB_CATEGORY = "streaming";

    /// <summary>
    /// Wall-clock stamp handed to Core when priming a channel's active sample. Fixed rather than
    /// <c>DateTime.Now</c> so the test never reads the system clock: these assertions are about
    /// whether a sample reaches the wrapper at all, so the instant is arbitrary and only needs to
    /// be stable.
    /// </summary>
    private static readonly DateTime SAMPLE_WALL_CLOCK = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private Mock<IAppLogger> _logger = null!;
    private DiscardDiagnosticsTestDevice _device = null!;

    [TestInitialize]
    public void Setup()
    {
        _logger = new Mock<IAppLogger>();
        _device = new DiscardDiagnosticsTestDevice(_logger.Object)
        {
            StreamingFrequency = STREAMING_FREQUENCY_HZ
        };
    }

    // MSTest disposes the test-class instance after each test, releasing the device's Core
    // connection instead of leaking one per test (CA1001).
    public void Dispose()
    {
        _device.Dispose();
        GC.SuppressFinalize(this);
    }

    [TestMethod]
    public void StreamRestart_LeftoverFrameFromPreviousSession_IsNeverDispatched()
    {
        // Arrange - a first session establishes the counter reference Core's gate arms against
        _device.InitializeStreaming();
        _device.RouteStreamFrame(1_000_000_000);
        _device.RouteStreamFrame(1_000_000_000 + SAMPLE_PERIOD_TICKS);
        _device.StopStreaming();
        var dispatchedBeforeRestart = _device.DispatchedMessages.Count;
        Assert.AreEqual(2, dispatchedBeforeRestart, "Precondition: both frames of the first session should flow.");

        // Act - restart; the device emits the latched prior-session frame first (one sample period
        // after the last frame it sent), then genuine frames offset by the 13 s stop-to-start gap
        _device.InitializeStreaming();
        var leftoverTimestamp = 1_000_000_000 + 2 * SAMPLE_PERIOD_TICKS;
        _device.RouteStreamFrame(leftoverTimestamp);
        var leftoverWasDiscarded = _device.DispatchedMessages.Count == dispatchedBeforeRestart;

        _device.RouteStreamFrame(leftoverTimestamp + THIRTEEN_SECOND_GAP_TICKS);
        _device.RouteStreamFrame(leftoverTimestamp + THIRTEEN_SECOND_GAP_TICKS + SAMPLE_PERIOD_TICKS);

        // Assert
        Assert.IsTrue(leftoverWasDiscarded, "The latched prior-session frame should never reach the desktop.");
        Assert.AreEqual(dispatchedBeforeRestart + 2, _device.DispatchedMessages.Count,
            "Both genuine frames should be processed.");

        var firstGenuine = _device.DispatchedMessages[^2];
        var secondGenuine = _device.DispatchedMessages[^1];
        var deltaSeconds = (secondGenuine.TimestampTicks - firstGenuine.TimestampTicks)
            / (double)TimeSpan.TicksPerSecond;
        Assert.IsTrue(deltaSeconds > 0 && deltaSeconds < 1.0,
            $"The session should anchor on genuine data, not span the stop-to-start gap (was {deltaSeconds:F4}s).");
    }

    [TestMethod]
    public void StreamRestart_LeftoverFrameAcrossCounterWrap_IsNeverDispatched()
    {
        // Arrange - the last frame of the first session sits just below the 32-bit counter wrap.
        // Without rejection this is the case that trips the false-positive rollover branch and puts
        // backward time on the axis.
        const uint nearWrapTimestamp = 4_294_800_000;
        _device.InitializeStreaming();
        _device.RouteStreamFrame(nearWrapTimestamp - SAMPLE_PERIOD_TICKS);
        _device.RouteStreamFrame(nearWrapTimestamp);
        _device.StopStreaming();
        var dispatchedBeforeRestart = _device.DispatchedMessages.Count;

        // Act - the latched frame wraps the counter; genuine frames follow 13 s later
        _device.InitializeStreaming();
        var leftoverTimestamp = unchecked(nearWrapTimestamp + SAMPLE_PERIOD_TICKS);
        _device.RouteStreamFrame(leftoverTimestamp);
        var leftoverWasDiscarded = _device.DispatchedMessages.Count == dispatchedBeforeRestart;

        var firstGenuineTimestamp = unchecked(leftoverTimestamp + THIRTEEN_SECOND_GAP_TICKS);
        _device.RouteStreamFrame(firstGenuineTimestamp);
        _device.RouteStreamFrame(unchecked(firstGenuineTimestamp + SAMPLE_PERIOD_TICKS));

        // Assert
        Assert.IsTrue(leftoverWasDiscarded, "The wrapped latched frame should never reach the desktop.");
        Assert.AreEqual(dispatchedBeforeRestart + 2, _device.DispatchedMessages.Count);
        Assert.IsTrue(_device.DispatchedMessages[^1].TimestampTicks > _device.DispatchedMessages[^2].TimestampTicks,
            "Time must move forward between genuine frames after a counter wrap at session start.");
    }

    [TestMethod]
    public void StreamRestart_GenuineFirstFrameBeyondWindow_IsProcessedImmediately()
    {
        // The guard in the other direction: a device that behaved (no latched frame) must not have
        // its first frame withheld. This is the assertion Core's sample-period window makes much
        // safer than the desktop's old fixed 2.5 s one - at 100 Hz the window is 25 ms, so a
        // genuine restart is nowhere near it.
        _device.InitializeStreaming();
        _device.RouteStreamFrame(1_000_000_000);
        _device.StopStreaming();
        var dispatchedBeforeRestart = _device.DispatchedMessages.Count;

        _device.InitializeStreaming();
        _device.RouteStreamFrame(1_000_000_000 + THIRTEEN_SECOND_GAP_TICKS);

        Assert.AreEqual(dispatchedBeforeRestart + 1, _device.DispatchedMessages.Count,
            "A genuine first frame beyond the detection window should be processed immediately.");
    }

    [TestMethod]
    public void DiscardedFrame_IsRecordedAsBreadcrumb_AndNeverAsError()
    {
        // Arrange
        _device.InitializeStreaming();
        _device.RouteStreamFrame(2_000_000_000);
        _device.StopStreaming();
        _device.InitializeStreaming();

        // Act
        _device.RouteStreamFrame(2_000_000_000 + SAMPLE_PERIOD_TICKS);

        // Assert - discards are EXPECTED on affected firmware (one leftover per restart), so the
        // Error path is the wrong home for them: it is the only path that captures to Sentry, and
        // it would file an issue per streaming session for a device-side condition no app change
        // can fix. This repo has flooded Sentry that way three times already (#775, #779, #801).
        // The signal still has to exist though - it is what the deleted discard counter provided -
        // so it lands on the Sentry timeline as a debug breadcrumb instead.
        _logger.Verify(
            l => l.AddBreadcrumb(
                BREADCRUMB_CATEGORY, It.Is<string>(m => m.Contains("discarded")), BreadcrumbLevel.Debug),
            Times.Once);

        // Both Error overloads, because both capture to Sentry — the exception overload via
        // CaptureException and the message-only one via a synthesized AppLogErrorException.
        _logger.Verify(l => l.Error(It.IsAny<string>()), Times.Never);
        _logger.Verify(l => l.Error(It.IsAny<Exception>(), It.IsAny<string>()), Times.Never);

        // Not a Warning either. Matched on the discard wording rather than blanket-verifying
        // Warning was never called: this session legitimately warns about the fallback tick period,
        // since the bare Core device under test reports no timestamp clock frequency.
        _logger.Verify(
            l => l.Warning(It.Is<string>(m => m.Contains("discarded"))),
            Times.Never);
    }

    [TestMethod]
    public void DiscardedFrame_ClosesTheChannelSampleGate()
    {
        // Arrange - a desktop channel wrapper subscribed to its Core channel, exactly as
        // SyncChannelsFromCore wires one up
        var coreChannel = BuildAnalogInputCoreChannel(0);
        var wrapper = new AnalogChannel(_device, coreChannel) { IsActive = true };
        _device.DataChannels.Add(wrapper);
        InvokeSubscribeChannelSamples(_device, wrapper, coreChannel);

        _device.InitializeStreaming();
        _device.RouteStreamFrame(3_000_000_000);
        coreChannel.SetActiveSample(1.25, SAMPLE_WALL_CLOCK);
        Assert.IsNotNull(wrapper.ActiveSample,
            "Precondition: an accepted frame opens the gate, so Core's decode of it reaches the wrapper.");

        _device.StopStreaming();
        Assert.IsNull(wrapper.ActiveSample, "Precondition: stopping the stream clears the active sample.");

        // Act - restart, then a latched frame Core withholds. Core raises StreamFrameDiscarded
        // synchronously before decoding it, and a PartialAnalogFrame discard is still decoded for
        // its digital payload - so a decoded sample can follow a discard. The desktop never sees
        // the frame itself, which is why the discard has to close the gate.
        _device.InitializeStreaming();
        _device.RouteStreamFrame(3_000_000_000 + SAMPLE_PERIOD_TICKS);
        coreChannel.SetActiveSample(9.99, SAMPLE_WALL_CLOCK.AddSeconds(1));

        // Assert
        Assert.IsNull(wrapper.ActiveSample,
            "A sample decoded from a discarded frame must not reach the wrapper - it would carry " +
            "the previously accepted frame's timestamp, which belongs to another session.");
    }

    [TestMethod]
    public void UnsubscribeCoreDeviceEvents_StopsDiscardsFromReachingTheDevice()
    {
        // Arrange - a session primed to discard its next frame
        _device.InitializeStreaming();
        _device.RouteStreamFrame(4_000_000_000);
        _device.StopStreaming();
        _device.InitializeStreaming();

        // Act - tear the subscriptions down the way Disconnect/CleanupConnection do. The
        // start/stop breadcrumbs from the arrange phase are irrelevant here.
        _logger.Invocations.Clear();
        InvokeUnsubscribeCoreDeviceEvents(_device, _device.CoreStreamingDeviceForTest);
        _device.RouteStreamFrame(4_000_000_000 + SAMPLE_PERIOD_TICKS);

        // Assert - a discard really did happen (Core counts every one whether or not anyone is
        // subscribed), and the desktop heard nothing about it. Without the count assertion this
        // test would also pass if the frame had simply been accepted.
        Assert.AreEqual(1L, _device.CoreStreamingDeviceForTest.DiscardedStreamFrameCount,
            "Precondition: Core should still have discarded the leftover frame.");
        _logger.Verify(
            l => l.AddBreadcrumb(It.IsAny<string>(), It.IsAny<string>(), BreadcrumbLevel.Debug),
            Times.Never);
    }

    private static Daqifi.Core.Channel.AnalogChannel BuildAnalogInputCoreChannel(int index)
    {
        return new Daqifi.Core.Channel.AnalogChannel(index, 4096)
        {
            Name = $"AI{index}",
            Direction = Daqifi.Core.Channel.ChannelDirection.Input,
            CalibrationB = 0,
            CalibrationM = 1,
            InternalScaleM = 1,
            PortRange = 5
        };
    }

    /// <summary>
    /// Calls one of the device's private wiring helpers. They are deliberately private — their
    /// production callers are the connect/disconnect template and <c>SyncChannelsFromCore</c> — so
    /// the tests reach them by reflection rather than widening the device's API.
    /// </summary>
    private static void InvokePrivateMethod(AbstractStreamingDevice device, string methodName, params object[] args)
    {
        var method = typeof(AbstractStreamingDevice).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.IsNotNull(method, $"{methodName} was not found on AbstractStreamingDevice.");
        method.Invoke(device, args);
    }

    private static void InvokeSubscribeChannelSamples(
        AbstractStreamingDevice device,
        IChannel desktopChannel,
        Daqifi.Core.Channel.IChannel coreChannel)
    {
        InvokePrivateMethod(device, "SubscribeChannelSamples", desktopChannel, coreChannel);
    }

    private static void InvokeUnsubscribeCoreDeviceEvents(
        AbstractStreamingDevice device,
        CoreStreamingDevice coreDevice)
    {
        InvokePrivateMethod(device, "UnsubscribeCoreDeviceEvents", coreDevice);
    }

    /// <summary>
    /// Test device that routes protobuf frames through Core's real stream-frame handling — gate
    /// included — and records device-message dispatches instead of touching the LoggingManager
    /// singleton.
    /// </summary>
    private sealed class DiscardDiagnosticsTestDevice : AbstractStreamingDevice, IDisposable
    {
        private readonly RoutableCoreStreamingDevice _coreDevice;

        public DiscardDiagnosticsTestDevice(IAppLogger appLogger)
        {
            AppLogger = appLogger;
            _coreDevice = new RoutableCoreStreamingDevice();
            _coreDevice.Connect();
            CoreDevice = _coreDevice;

            // The wiring under test: the desktop hears about withheld frames only because
            // SubscribeCoreDeviceEvents attaches OnCoreStreamFrameDiscarded.
            SubscribeCoreDeviceEvents(_coreDevice);
        }

        // The Core device connected in the constructor is owned by this fixture; disposing it keeps
        // the suite from leaking one connected device per test (CA1001).
        public void Dispose() => _coreDevice.Dispose();

        public CoreStreamingDevice CoreStreamingDeviceForTest => _coreDevice;

        public List<DeviceMessage> DispatchedMessages { get; } = [];

        public override ConnectionType ConnectionType => ConnectionType.Usb;

        public override bool Connect() => true;

        public override bool Disconnect() => true;

        public override bool Write(string command) => true;

        protected override void SendMessage(IOutboundMessage<string> message)
        {
        }

        protected override void DispatchDeviceMessage(DeviceMessage deviceMessage)
        {
            DispatchedMessages.Add(deviceMessage);
        }

        /// <summary>
        /// Hands a frame to Core exactly as its transport would. Core screens it, and only then —
        /// if it survives — raises the classified <c>StreamMessageReceived</c> this device is
        /// subscribed to.
        /// </summary>
        public void RouteStreamFrame(uint deviceTimestamp)
        {
            _coreDevice.RouteStreamFrame(new DaqifiOutMessage
            {
                MsgTimeStamp = deviceTimestamp,
                DeviceSn = 12345,
                DeviceFwRev = "1.0.0",
                AnalogInDataFloat = { 1.25f }
            });
        }
    }

    /// <summary>
    /// Core streaming device with its inbound stream-frame entry point exposed, so a test can feed
    /// frames through Core's own screening without a transport.
    /// </summary>
    private sealed class RoutableCoreStreamingDevice() : CoreStreamingDevice("TestDevice")
    {
        public void RouteStreamFrame(DaqifiOutMessage message) => OnStreamMessageReceived(message);

        public override void Send<T>(IOutboundMessage<T> message)
        {
        }
    }
}
