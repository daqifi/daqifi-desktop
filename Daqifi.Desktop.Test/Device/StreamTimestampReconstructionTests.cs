using System.Globalization;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
using Daqifi.Core.Communication.Messages;
using Daqifi.Core.Device;
using CoreStreamingDevice = Daqifi.Core.Device.DaqifiStreamingDevice;

namespace Daqifi.Desktop.Test.Device;

/// <summary>
/// Covers how a streaming session reconstructs frame timestamps from the device-reported clock
/// frequency, and what happens when the device never reports one.
/// <para>
/// Replaces the interleaving fixture issue #782 needed. That bug was a desktop-side gate — "have I
/// applied the frequency yet" — that could latch over a frequency a UI-thread <c>ResetAll()</c> had
/// just dropped, silently stranding the whole session on Core's 50 MHz fallback against firmware's
/// 42 MHz timer. Core v1.4.0's <c>ResetAll()</c> keeps per-device frequencies
/// (daqifi-core#398 gap 3), so the gate and the lock that guarded it are gone. What remains worth
/// asserting is the user-visible outcome they existed for: a stop/start must leave the restarted
/// session reconstructing on the device's own clock, and a device that publishes no clock at all
/// must say so instead of quietly rescaling every timestamp.
/// </para>
/// </summary>
[TestClass]
public class StreamTimestampReconstructionTests : IDisposable
{
    #region Constants
    /// <summary>
    /// Clock frequency the fixture's device reports, matching the bench hardware: an Nq1 on firmware
    /// 3.7.2 reports 42 MHz, not the 50 MHz Core falls back to when no frequency is applied.
    /// </summary>
    private const uint DEVICE_TIMESTAMP_FREQUENCY = 42_000_000;

    /// <summary>One 100 Hz sample period, in device counter ticks at the frequency above.</summary>
    private const uint SAMPLE_PERIOD_TICKS = DEVICE_TIMESTAMP_FREQUENCY / 100;

    /// <summary>
    /// A stop-to-start gap in device counter ticks, comfortably past the leftover-frame window so the
    /// restarted session's frames are accepted rather than discarded as prior-session leftovers.
    /// </summary>
    private const uint RESTART_GAP_TICKS = DEVICE_TIMESTAMP_FREQUENCY * 13;

    /// <summary>Arbitrary starting value of the device's free-running counter.</summary>
    private const uint FIRST_FRAME_TIMESTAMP = 1_000_000_000;

    /// <summary>Substring that identifies the fallback-tick-period warning among routine logging.</summary>
    private const string FALLBACK_WARNING_MARKER = "no timestamp clock frequency";
    #endregion

    #region Fields
    private StreamingTestDevice? _device;
    #endregion

    #region Setup and Teardown
    /// <summary>
    /// Releases the fixture's Core connection. MSTest disposes the test-class instance after each
    /// test, so this keeps the suite from leaking one connected device per test (CA1001).
    /// </summary>
    public void Dispose()
    {
        _device?.Dispose();
        GC.SuppressFinalize(this);
    }
    #endregion

    #region Tests
    [TestMethod]
    public void StopStart_LeavesTheRestartedSessionOnTheDeviceReportedClock()
    {
        // Arrange — a session that has applied the device's 42 MHz clock, then a stop/start of the
        // kind a user triggers from the UI thread.
        _device = new StreamingTestDevice(publishTimestampFrequency: true);
        _device.InitializeStreaming();
        _device.RouteStreamFrame(FIRST_FRAME_TIMESTAMP);
        _device.RouteStreamFrame(FIRST_FRAME_TIMESTAMP + SAMPLE_PERIOD_TICKS);

        _device.StopStreaming();
        _device.InitializeStreaming();

        // Act — two frames of the restarted session exactly one device-second apart at the reported
        // frequency, a restart gap past the first session's last counter value.
        var restartTimestamp = FIRST_FRAME_TIMESTAMP + SAMPLE_PERIOD_TICKS + RESTART_GAP_TICKS;
        _device.RouteStreamFrame(restartTimestamp);
        _device.RouteStreamFrame(restartTimestamp + DEVICE_TIMESTAMP_FREQUENCY);

        // Assert — a session that lost the frequency across the reset would reconstruct this pair
        // 0.84 s apart against the 50 MHz fallback, with the error compounding because each
        // timestamp is the previous one plus elapsed ticks.
        Assert.IsTrue(_device.DispatchedMessages.Count >= 2, "The restarted session should have dispatched frames.");
        var deltaTicks = _device.DispatchedMessages[^1].TimestampTicks - _device.DispatchedMessages[^2].TimestampTicks;
        var deltaSeconds = deltaTicks / (double)TimeSpan.TicksPerSecond;
        Assert.AreEqual(1.0, deltaSeconds, 1e-6,
            "One device-second of counter ticks should reconstruct to one second after a stop/start.");
        Assert.AreEqual(1.0 / DEVICE_TIMESTAMP_FREQUENCY, _device.EffectiveTickPeriod, 1e-15,
            "The restarted session should still be on the device-reported frequency rather than " +
            "silently falling back to Core's 20 ns default.");
    }

    [TestMethod]
    public void StopStart_DoesNotReapplyTheFrequencyItAlreadyHas()
    {
        // Arrange — the apply is now once per device, not once per session: Core keeps the frequency
        // across ResetAll, and HasTimestampFrequency is the record of whether it happened. A per-frame
        // or per-session re-apply would be wasted work on the hot streaming path.
        _device = new StreamingTestDevice(publishTimestampFrequency: true);
        _device.InitializeStreaming();
        _device.RouteStreamFrame(FIRST_FRAME_TIMESTAMP);
        _device.RouteStreamFrame(FIRST_FRAME_TIMESTAMP + SAMPLE_PERIOD_TICKS);
        Assert.AreEqual(1, _device.FrequencyApplyCount, "Precondition: the first session applied it once.");

        // Act
        _device.StopStreaming();
        _device.InitializeStreaming();
        var restartTimestamp = FIRST_FRAME_TIMESTAMP + SAMPLE_PERIOD_TICKS + RESTART_GAP_TICKS;
        _device.RouteStreamFrame(restartTimestamp);
        _device.RouteStreamFrame(restartTimestamp + SAMPLE_PERIOD_TICKS);

        // Assert
        Assert.AreEqual(1, _device.FrequencyApplyCount);
    }

    [TestMethod]
    public void WhenTheDeviceReportsNoFrequency_WarnsOnceAndNeverAtError()
    {
        // Arrange — older firmware that omits timestamp_freq. Core reconstructs against its 50 MHz
        // fallback and, before v1.4.0 exposed UsedFallbackTickPeriod, said nothing at all: every
        // timestamp came out scaled by the ratio of the real clock to 50 MHz with no error, log, or
        // warning (issue #782).
        _device = new StreamingTestDevice(publishTimestampFrequency: false);
        _device.InitializeStreaming();

        // Act — several frames, so a per-frame warning would show up as a flood.
        for (var i = 0; i < 5; i++)
        {
            _device.RouteStreamFrame(FIRST_FRAME_TIMESTAMP + ((uint)i * SAMPLE_PERIOD_TICKS));
        }

        // Assert
        Assert.AreEqual(1, _device.FallbackWarnings.Count,
            "The condition is static device configuration, so repeating it per frame would bury the log.");
        Assert.AreEqual(0, _device.ErrorCount,
            "Error is the Sentry path, and no app change can fix a device that does not report its clock.");
    }

    [TestMethod]
    public void WhenTheDeviceReportsItsFrequency_DoesNotWarnAboutTheFallback()
    {
        // Arrange — the healthy case. Regression guard: a warning here would fire on every session
        // on every supported device.
        _device = new StreamingTestDevice(publishTimestampFrequency: true);
        _device.InitializeStreaming();

        // Act
        _device.RouteStreamFrame(FIRST_FRAME_TIMESTAMP);
        _device.RouteStreamFrame(FIRST_FRAME_TIMESTAMP + SAMPLE_PERIOD_TICKS);

        // Assert
        Assert.AreEqual(0, _device.FallbackWarnings.Count);
    }
    #endregion

    #region Test Doubles
    /// <summary>
    /// A transport-less device that routes synthetic stream frames through the desktop's inbound
    /// path and collects what came out.
    /// </summary>
    private sealed class StreamingTestDevice : AbstractStreamingDevice, IDisposable
    {
        private const uint DEVICE_SERIAL_NUMBER = 12345;
        private const int ANALOG_PORT_COUNT = 1;

        private readonly SilentCoreDevice _coreDevice;
        private readonly CountingTimestampProcessor _processor;
        private readonly RecordingAppLogger _logger;

        /// <param name="publishTimestampFrequency">
        /// Whether the device's status message carries <c>timestamp_freq</c>. False models firmware
        /// that omits it, which is what puts Core on its fallback tick period.
        /// </param>
        public StreamingTestDevice(bool publishTimestampFrequency)
        {
            _processor = new CountingTimestampProcessor(new TimestampProcessor());
            _logger = new RecordingAppLogger();

            // Both dependencies are init-only precisely so a test double can substitute an
            // instrumented one here; nothing can swap them afterwards.
            FrameTimestampProcessor = _processor;
            AppLogger = _logger;

            _coreDevice = new SilentCoreDevice();
            _coreDevice.Connect();

            var statusMessage = new DaqifiOutMessage
            {
                DevicePn = "Nq1",
                DeviceSn = DEVICE_SERIAL_NUMBER,
                DeviceFwRev = "3.7.2",
                AnalogInPortNum = ANALOG_PORT_COUNT,
                AnalogInRes = 4095,
                TimestampFreq = publishTimestampFrequency ? DEVICE_TIMESTAMP_FREQUENCY : 0,
            };
            statusMessage.AnalogInCalM.Add(1.0f);
            statusMessage.AnalogInCalB.Add(0.0f);
            statusMessage.AnalogInIntScaleM.Add(1.0f);
            statusMessage.AnalogInPortRange.Add(5.0f);

            _coreDevice.Metadata.UpdateFromProtobuf(statusMessage);

            // What actually publishes Core's TimestampFrequency — and therefore what the desktop
            // reads when deciding which frequency to apply. Metadata alone leaves it at zero.
            _coreDevice.PopulateChannelsFromStatus(statusMessage);
        }

        public List<DeviceMessage> DispatchedMessages { get; } = [];

        /// <summary>How many times the device pushed a frequency into the processor.</summary>
        public int FrequencyApplyCount => _processor.SetFrequencyCallCount;

        /// <summary>Warnings that named the fallback tick period, ignoring routine logging.</summary>
        public IReadOnlyList<string> FallbackWarnings => _logger.FallbackWarnings;

        /// <summary>Every Error-level call, which is the Sentry path.</summary>
        public int ErrorCount => _logger.ErrorCount;

        /// <summary>
        /// The tick period Core would use for this device's frames — the device-reported one when a
        /// frequency is applied, and the silent 20 ns fallback when it is not.
        /// </summary>
        public double EffectiveTickPeriod =>
            _processor.GetTickPeriod(DEVICE_SERIAL_NUMBER.ToString(CultureInfo.InvariantCulture));

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
        /// Routes a frame through the desktop's inbound path — the handler Core invokes when it
        /// raises the classified <c>StreamMessageReceived</c> event. Core's own decode step is left
        /// out; these tests read the dispatched <see cref="DeviceMessage"/>s rather than channel
        /// samples.
        /// </summary>
        public void RouteStreamFrame(uint deviceTimestamp) => OnStreamMessageReceived(new DaqifiOutMessage
        {
            MsgTimeStamp = deviceTimestamp,
            DeviceSn = DEVICE_SERIAL_NUMBER,
            DeviceFwRev = "3.7.2",
            AnalogInDataFloat = { 1.25f }
        });

        public void Dispose() => _coreDevice.Dispose();
    }

    /// <summary>
    /// Forwards every call to a real <see cref="TimestampProcessor"/>, counting the frequency
    /// applies so a re-apply per session or per frame is visible.
    /// </summary>
    private sealed class CountingTimestampProcessor(ITimestampProcessor inner) : ITimestampProcessor
    {
        public int SetFrequencyCallCount { get; private set; }

        public double TickPeriod => inner.TickPeriod;

        public void SetTimestampFrequency(string deviceId, uint frequencyHz)
        {
            SetFrequencyCallCount++;
            inner.SetTimestampFrequency(deviceId, frequencyHz);
        }

        public double GetTickPeriod(string deviceId) => inner.GetTickPeriod(deviceId);

        public bool HasTimestampFrequency(string deviceId) => inner.HasTimestampFrequency(deviceId);

        public TimestampResult ProcessTimestamp(string deviceId, uint deviceTimestamp) =>
            inner.ProcessTimestamp(deviceId, deviceTimestamp);

        public void Reset(string deviceId) => inner.Reset(deviceId);

        public void ResetAll() => inner.ResetAll();
    }

    /// <summary>
    /// Captures the log calls these tests assert on and swallows the rest.
    /// </summary>
    /// <remarks>
    /// The device under test logs routinely (sleep-state changes, streaming breadcrumbs) and none of
    /// it is what these tests observe. Swallowing it also keeps the suite from writing to the real
    /// NLog sink for diagnostics no one reads.
    /// </remarks>
    private sealed class RecordingAppLogger : IAppLogger
    {
        private readonly List<string> _fallbackWarnings = [];

        public IReadOnlyList<string> FallbackWarnings => _fallbackWarnings;

        public int ErrorCount { get; private set; }

        /// <summary>Records only the fallback-tick-period warning; ignores routine ones.</summary>
        /// <param name="message">The warning text.</param>
        public void Warning(string message)
        {
            if (message.Contains(FALLBACK_WARNING_MARKER, StringComparison.Ordinal))
            {
                _fallbackWarnings.Add(message);
            }
        }

        /// <summary>Records only the fallback-tick-period warning; ignores routine ones.</summary>
        /// <param name="ex">Ignored.</param>
        /// <param name="message">The warning text.</param>
        public void Warning(Exception ex, string message) => Warning(message);

        /// <summary>Counts the Sentry path.</summary>
        /// <param name="message">Ignored.</param>
        public void Error(string message) => ErrorCount++;

        /// <summary>Counts the Sentry path.</summary>
        /// <param name="ex">Ignored.</param>
        /// <param name="message">Ignored.</param>
        public void Error(Exception ex, string message) => ErrorCount++;

        /// <summary>No-op. See the class remarks.</summary>
        /// <param name="category">Ignored.</param>
        /// <param name="message">Ignored.</param>
        /// <param name="level">Ignored.</param>
        public void AddBreadcrumb(
            string category,
            string message,
            Common.Loggers.BreadcrumbLevel level = Common.Loggers.BreadcrumbLevel.Info)
        {
        }

        /// <summary>No-op. See the class remarks.</summary>
        /// <param name="message">Ignored.</param>
        public void Information(string message)
        {
        }

        /// <summary>No-op. See the class remarks.</summary>
        /// <param name="model">Ignored.</param>
        /// <param name="serialNumber">Ignored.</param>
        /// <param name="firmwareVersion">Ignored.</param>
        /// <param name="connectionType">Ignored.</param>
        /// <param name="activeChannels">Ignored.</param>
        public void SetDeviceContext(
            string model, string serialNumber, string firmwareVersion, string connectionType, int activeChannels)
        {
        }

        /// <summary>No-op. See the class remarks.</summary>
        public void ClearDeviceContext()
        {
        }

        /// <summary>No-op. See the class remarks.</summary>
        public void Shutdown()
        {
        }
    }

    /// <summary>A transport-less Core streaming device: commands are accepted and dropped.</summary>
    private sealed class SilentCoreDevice() : CoreStreamingDevice("TestDevice")
    {
        public override void Send<T>(IOutboundMessage<T> message)
        {
        }
    }
    #endregion
}
