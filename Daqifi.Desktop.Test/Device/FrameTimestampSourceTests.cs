using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Device;
using Daqifi.Core.Communication.Messages;
using Daqifi.Core.Device;
using ChannelType = Daqifi.Core.Channel.ChannelType;
using CoreStreamingDevice = Daqifi.Core.Device.DaqifiStreamingDevice;

namespace Daqifi.Desktop.Test.Device;

/// <summary>
/// Tests that one stream frame yields exactly one timestamp (issue #769): the reconstruction
/// <c>ProcessStreamMessage</c> computes for a frame is what both the dispatched
/// <see cref="DeviceMessage"/> and every channel sample decoded from that frame carry, and that it
/// is scaled by the device's own clock frequency rather than the 50 MHz fallback.
/// <para>
/// This needs a fixture no existing one provides: desktop channels wired onto Core's real decode
/// pipeline (so <c>ActiveSample</c> is populated, as in <c>ChannelDataMappingTests</c>),
/// <em>plus</em> captured <see cref="DeviceMessage"/>s (as in <c>StreamFrameDiscardDiagnosticsTests</c>),
/// <em>plus</em> the real <c>InitializeStreaming</c>/<c>StopStreaming</c> session lifecycle, which is
/// what resets the desktop's timestamp baseline.
/// </para>
/// </summary>
[TestClass]
public class FrameTimestampSourceTests : IDisposable
{
    #region Constants
    /// <summary>
    /// Clock frequency the fixture's device reports, matching the bench hardware: an Nq1 on
    /// firmware 3.7.2 reports 42 MHz (84 MHz PBCLK, 1:2 prescale), not the 50 MHz that Core's
    /// <c>TimestampProcessor</c> falls back to when no frequency is applied.
    /// </summary>
    private const uint DEVICE_TIMESTAMP_FREQUENCY = 42_000_000;

    /// <summary>One 100 Hz sample period, in device counter ticks at the frequency above.</summary>
    private const uint SAMPLE_PERIOD_TICKS = DEVICE_TIMESTAMP_FREQUENCY / 100;

    /// <summary>A 13 s stop-to-start gap, in device counter ticks.</summary>
    private const uint THIRTEEN_SECOND_GAP_TICKS = DEVICE_TIMESTAMP_FREQUENCY * 13;
    #endregion

    #region Fields
    private FrameTimestampTestDevice _device = null!;
    private AnalogChannel _channel = null!;
    #endregion

    #region Setup and Teardown
    [TestInitialize]
    public void Setup()
    {
        _device = new FrameTimestampTestDevice();
        _channel = (AnalogChannel)_device.DataChannels.Single(
            c => c.Type == ChannelType.Analog && c.Index == 0);
        _channel.IsActive = true;
    }

    // MSTest disposes the test-class instance after each test, releasing the device's Core
    // connection instead of leaking one per test (CA1001).
    public void Dispose()
    {
        _device.Dispose();
        GC.SuppressFinalize(this);
    }
    #endregion

    #region Tests
    [TestMethod]
    public void SingleSession_ChannelSampleCarriesTheTimestampOfItsOwnFrame()
    {
        // Arrange
        _device.InitializeStreaming();

        // Act - three frames leave the third as the most recent one on both paths
        _device.RouteStreamFrame(1_000_000_000);
        _device.RouteStreamFrame(1_000_000_000 + SAMPLE_PERIOD_TICKS);
        _device.RouteStreamFrame(1_000_000_000 + 2 * SAMPLE_PERIOD_TICKS);

        // Assert - the sample stored for the frame and the message dispatched for it are the
        // same frame, so they must not carry two independently reconstructed timestamps
        Assert.IsNotNull(_channel.ActiveSample, "The last accepted frame should produce a channel sample.");
        var dispatched = _device.DispatchedMessages[^1];
        Assert.AreEqual(dispatched.TimestampTicks, _channel.ActiveSample.TimestampTicks,
            "The channel sample and the DeviceMessage dispatched for the same frame should share one timestamp.");
    }

    [TestMethod]
    public void StreamRestart_ChannelSampleDoesNotCarryTheStopToStartGap()
    {
        // Arrange - a session runs and stops, establishing a counter reference
        _device.InitializeStreaming();
        _device.RouteStreamFrame(1_000_000_000);
        _device.RouteStreamFrame(1_000_000_000 + SAMPLE_PERIOD_TICKS);
        _device.StopStreaming();

        // Act - on restart the device emits the latched prior-session frame first (withheld by
        // Core's stream-frame gate), then genuine frames a 13 s gap later
        _device.InitializeStreaming();
        var leftoverTimestamp = 1_000_000_000 + 2 * SAMPLE_PERIOD_TICKS;
        _device.RouteStreamFrame(leftoverTimestamp);
        _device.RouteStreamFrame(leftoverTimestamp + THIRTEEN_SECOND_GAP_TICKS);
        _device.RouteStreamFrame(leftoverTimestamp + THIRTEEN_SECOND_GAP_TICKS + SAMPLE_PERIOD_TICKS);

        // Assert - a baseline anchored on the discarded leftover frame would push every sample of
        // the new session 13 s into the future, and DataSample is what gets persisted (issue #573)
        Assert.IsNotNull(_channel.ActiveSample, "Genuine frames after a restart should produce channel samples.");
        var dispatched = _device.DispatchedMessages[^1];
        var driftSeconds = (_channel.ActiveSample.TimestampTicks - dispatched.TimestampTicks)
            / (double)TimeSpan.TicksPerSecond;
        Assert.AreEqual(dispatched.TimestampTicks, _channel.ActiveSample.TimestampTicks,
            $"Restarted-session samples should not be shifted by the stop-to-start gap (drift {driftSeconds:F4}s).");
    }

    [TestMethod]
    public void DeviceReportedClockFrequency_ScalesTheReconstructedTimeline()
    {
        // Arrange - a session on a device that reports its own 42 MHz streaming clock
        _device.InitializeStreaming();

        // Act - two frames exactly one device-second apart at that reported frequency
        _device.RouteStreamFrame(500_000_000);
        _device.RouteStreamFrame(500_000_000 + DEVICE_TIMESTAMP_FREQUENCY);

        // Assert - reconstructed against the 50 MHz fallback this pair reads 0.84 s apart, and the
        // error compounds, because each timestamp is the previous one plus elapsed ticks
        Assert.AreEqual(2, _device.DispatchedMessages.Count,
            "Both frames of the first session should be dispatched.");
        var deltaTicks = _device.DispatchedMessages[1].TimestampTicks
            - _device.DispatchedMessages[0].TimestampTicks;
        var deltaSeconds = deltaTicks / (double)TimeSpan.TicksPerSecond;
        Assert.AreEqual(1.0, deltaSeconds, 1e-6,
            "One device-second of counter ticks should reconstruct to one second.");

        Assert.IsNotNull(_channel.ActiveSample);
        Assert.IsNotNull(_channel.ActiveSample.FirmwareDeltaMs);
        Assert.AreEqual(1000.0, _channel.ActiveSample.FirmwareDeltaMs.Value, 1e-3,
            "The firmware-measured delta should be scaled by the device's own clock frequency.");
    }

    [TestMethod]
    public void RoutingAFrame_FinishesProcessingIt_BeforeCoreDecodesTheSameFrame()
    {
        // Arrange - a session already past its first frames, so the next frame routed is an
        // ordinary accepted one
        _device.InitializeStreaming();
        _device.RouteStreamFrame(1_000_000_000);
        _device.RouteStreamFrame(1_000_000_000 + SAMPLE_PERIOD_TICKS);
        var dispatchedBefore = _device.DispatchedMessages.Count;

        // Act - only the desktop half of the pipeline: the handler Core invokes when it raises the
        // classified StreamMessageReceived event, before going on to decode that same frame into
        // per-channel samples
        _device.RouteStreamFrameToDesktopOnly(1_000_000_000 + 2 * SAMPLE_PERIOD_TICKS);

        // Assert - Core decodes the frame immediately after this handler returns, and that decode
        // reads per-frame state the handler leaves behind (sample gate, timestamp, firmware delta).
        // That state only belongs to the right frame if the handler completes synchronously, so an
        // await introduced inside it would silently let Core decode against the previous frame's state.
        Assert.AreEqual(dispatchedBefore + 1, _device.DispatchedMessages.Count,
            "Routing a frame must finish processing it before returning, since Core decodes the same "
            + "frame immediately afterwards and reads the per-frame state left behind.");
    }
    #endregion

    #region Test Doubles
    /// <summary>
    /// Routes frames through Core's real <c>OnStreamMessageReceived</c>, which raises the classified
    /// <c>StreamMessageReceived</c> event the desktop's gating/dispatch subscribes to and then runs
    /// Core's decode step — in that order, exactly as production sequences them.
    /// </summary>
    private sealed class FrameTimestampTestDevice : AbstractStreamingDevice, IDisposable
    {
        private const int ANALOG_PORT_COUNT = 2;
        private const uint DEVICE_SERIAL_NUMBER = 12345;

        private readonly FrameTimestampCoreDevice _coreDevice;

        public FrameTimestampTestDevice()
        {
            _coreDevice = new FrameTimestampCoreDevice();
            _coreDevice.Connect();

            var statusMessage = new DaqifiOutMessage
            {
                DevicePn = "Nq1",
                DeviceSn = DEVICE_SERIAL_NUMBER,
                DeviceFwRev = "1.0.0",
                AnalogInPortNum = ANALOG_PORT_COUNT,
                AnalogInRes = 4095,
                TimestampFreq = DEVICE_TIMESTAMP_FREQUENCY,
            };
            for (var i = 0; i < ANALOG_PORT_COUNT; i++)
            {
                statusMessage.AnalogInCalM.Add(1.0f);
                statusMessage.AnalogInCalB.Add(0.0f);
                statusMessage.AnalogInIntScaleM.Add(1.0f);
                statusMessage.AnalogInPortRange.Add(5.0f);
            }

            _coreDevice.Metadata.UpdateFromProtobuf(statusMessage);
            _coreDevice.PopulateChannelsFromStatus(statusMessage);

            // Wires the desktop channel wrappers around Core's actual channel instances, including
            // the SampleReceived subscription installed by SyncChannelsFromCore.
            SyncFromCoreDevice(_coreDevice);

            // Subscribes the wrapper to Core's classified Status/StreamMessageReceived events, which
            // is what Connect() does in production. Deliberately after PopulateChannelsFromStatus
            // above so the ChannelsPopulated subscription doesn't re-run the sync mid-setup.
            SubscribeCoreDeviceEvents(_coreDevice);
        }

        public List<DeviceMessage> DispatchedMessages { get; } = [];

        // The Core device connected in the constructor is owned by this fixture; disposing it keeps
        // the suite from leaking one connected device per test (CA1001).
        public void Dispose() => _coreDevice.Dispose();

        public override ConnectionType ConnectionType => ConnectionType.Usb;

        protected override CoreStreamingDevice? CoreDeviceForStreaming => _coreDevice;

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
        /// Routes a frame the way production does: Core's own <c>OnStreamMessageReceived</c> raises
        /// the classified <c>StreamMessageReceived</c> event this device is subscribed to, then runs
        /// Core's decode step. The desktop handler must not also be invoked directly here — it is
        /// already reached through that subscription, and calling both would process every frame twice.
        /// </summary>
        public void RouteStreamFrame(uint deviceTimestamp)
        {
            _coreDevice.SimulateStreamFrame(BuildStreamFrame(deviceTimestamp));
        }

        /// <summary>
        /// Routes a frame through the desktop's inbound path only, leaving Core's decode step out,
        /// so a test can observe what the desktop has finished doing by the time that path returns.
        /// </summary>
        public void RouteStreamFrameToDesktopOnly(uint deviceTimestamp)
        {
            RouteToDesktop(BuildStreamFrame(deviceTimestamp));
        }

        private void RouteToDesktop(DaqifiOutMessage message)
        {
            // Exactly what Core invokes when it raises the classified StreamMessageReceived event,
            // with Core's own decode step left out.
            OnStreamMessageReceived(message);
        }

        private static DaqifiOutMessage BuildStreamFrame(uint deviceTimestamp) => new()
        {
            MsgTimeStamp = deviceTimestamp,
            DeviceSn = DEVICE_SERIAL_NUMBER,
            DeviceFwRev = "1.0.0",
            AnalogInDataFloat = { 1.25f }
        };
    }

    private sealed class FrameTimestampCoreDevice() : CoreStreamingDevice("TestDevice")
    {
        public override void Send<T>(IOutboundMessage<T> message)
        {
        }

        public void SimulateStreamFrame(DaqifiOutMessage message) => OnStreamMessageReceived(message);
    }
    #endregion
}
