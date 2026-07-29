using System.Globalization;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
using Daqifi.Core.Communication.Messages;
using Daqifi.Core.Device;
using CoreStreamingDevice = Daqifi.Core.Device.DaqifiStreamingDevice;

namespace Daqifi.Desktop.Test.Device;

/// <summary>
/// Tests that a UI-thread stream stop/start can never interleave with the transport thread's
/// per-session clock-frequency apply (issue #782).
/// <para>
/// <c>ResetAll()</c> discards the device's reported clock configuration along with the session
/// baseline, so the frequency and the "already applied" gate have to change together. A reset landing
/// between the apply and the gate write leaves the gate latched with no frequency, and nothing reports
/// it — Core reverts to its processor-wide 20 ns (50 MHz) fallback silently — so the session just
/// reconstructs every timestamp scaled by 50/42 against firmware's 42 MHz streaming timer.
/// </para>
/// <para>
/// The interleave is driven deterministically rather than by a stress loop. The fixture substitutes an
/// instrumented <see cref="ITimestampProcessor"/> whose <c>SetTimestampFrequency</c> starts a real
/// stop/start on another thread, and an instrumented <see cref="IAppLogger"/> that reports when that
/// thread reaches the statement immediately preceding <c>StopStreaming</c>'s guarded region. The probe
/// therefore starts only once the reset is genuinely at the critical-section boundary, rather than
/// timing a thread that may not have got there yet.
/// </para>
/// </summary>
[TestClass]
public class TimestampProcessorSerializationTests : IDisposable
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

    /// <summary>
    /// How long the racing reset is watched for completion <em>after</em> it has already reached the
    /// statement immediately before the guarded region — so this measures blocking, not thread
    /// start-up latency. All that remains for an unserialized reset at that point is taking the lock
    /// and clearing two fields.
    /// </summary>
    private const int RESET_PROBE_TIMEOUT_MS = 250;

    /// <summary>Generous ceiling on waits that must succeed, so a hang fails as an assertion.</summary>
    private const int PROGRESS_TIMEOUT_MS = 30_000;

    /// <summary>
    /// The breadcrumb <c>StopStreaming</c> records on the statement immediately preceding its
    /// <c>ResetAll</c> + gate-clear region. Reaching it means the racing thread has finished every
    /// step before the lock and has nothing left to do but acquire it.
    /// </summary>
    private const string RESET_BOUNDARY_BREADCRUMB = "Streaming stopped";

    private const string SET_FREQUENCY_CALL = "SetTimestampFrequency";
    private const string PROCESS_TIMESTAMP_CALL = "ProcessTimestamp";
    private const string RESET_ALL_CALL = "ResetAll";
    #endregion

    #region Fields
    private InterleavingTestDevice _device = null!;
    #endregion

    #region Setup and Teardown
    /// <summary>Builds a fresh device fixture, with its own instrumented processor and logger.</summary>
    [TestInitialize]
    public void Setup()
    {
        _device = new InterleavingTestDevice();
    }

    /// <summary>
    /// Releases the fixture's Core connection. MSTest disposes the test-class instance after each
    /// test, so this keeps the suite from leaking one connected device per test (CA1001).
    /// </summary>
    public void Dispose()
    {
        _device.Dispose();
        GC.SuppressFinalize(this);
    }
    #endregion

    #region Tests
    [TestMethod]
    public void ResetRacingTheFrequencyApply_CannotLandBeforeTheGateIsWritten()
    {
        // Arrange — a session holding its first frame, with a stop/start armed to fire from inside
        // the frequency apply
        StartSessionAndArmTheRace();

        // Act — the successor frame releases the held pair, so the apply (and the armed race) happens
        // inside this call
        DriveTheRacedFrame();

        // Assert — the racing reset had reached the statement immediately before its guarded region
        // while the transport thread still held the frame open, so if it had completed there, the gate
        // write that follows would have latched "applied" over a frequency the reset just dropped
        Assert.IsTrue(_device.RacingResetReachedTheBoundary,
            "Precondition: the racing stop/start should have reached the reset's critical-section "
            + "boundary before the guarded region ended.");
        Assert.IsFalse(_device.RacingResetCompletedInsideTheGuardedRegion,
            "A stream stop/start must not be able to reset the timestamp processor while a frame is "
            + "between its frequency apply and the gate write that records it.");
    }

    [TestMethod]
    public void ResetRacingTheFrequencyApply_CannotSplitTheApplyFromTheReconstructionItWasAppliedFor()
    {
        // Arrange
        StartSessionAndArmTheRace();

        // Act
        DriveTheRacedFrame();

        // Assert — a reset between the apply and this frame's reconstruction would silently revert the
        // frame to the 50 MHz fallback tick and re-baseline it, so the apply and the ProcessTimestamp
        // it was performed for have to stay adjacent
        var calls = _device.ProcessorCalls;
        var applyIndex = calls.IndexOf(SET_FREQUENCY_CALL);
        Assert.AreNotEqual(-1, applyIndex, "The session should have applied the device-reported frequency.");
        Assert.IsTrue(applyIndex + 1 < calls.Count, "The apply should have been followed by a reconstruction.");
        Assert.AreEqual(PROCESS_TIMESTAMP_CALL, calls[applyIndex + 1],
            $"Nothing may run between the frequency apply and the frame's reconstruction; "
            + $"observed call order was [{string.Join(", ", calls)}].");
    }

    [TestMethod]
    public void ResetRacingTheFrequencyApply_LeavesTheRestartedSessionOnTheDeviceReportedClock()
    {
        // Arrange — a stop/start raced the previous session's frequency apply and has now landed, so
        // the restarted session starts with the processor's frequency table cleared
        StartSessionAndArmTheRace();
        DriveTheRacedFrame();

        // Act — two frames of the restarted session exactly one device-second apart at the reported
        // frequency, a restart gap past the racing session's last counter value
        var restartTimestamp = FIRST_FRAME_TIMESTAMP + SAMPLE_PERIOD_TICKS + RESTART_GAP_TICKS;
        _device.RouteStreamFrame(restartTimestamp);
        _device.RouteStreamFrame(restartTimestamp + DEVICE_TIMESTAMP_FREQUENCY);

        // Assert — a gate left latched over a dropped frequency never re-applies, so the whole
        // restarted session would reconstruct this pair 0.84 s apart against the 50 MHz fallback, with
        // the error compounding because each timestamp is the previous one plus elapsed ticks
        Assert.IsTrue(_device.DispatchedMessages.Count >= 2, "The restarted session should have dispatched frames.");
        var deltaTicks = _device.DispatchedMessages[^1].TimestampTicks - _device.DispatchedMessages[^2].TimestampTicks;
        var deltaSeconds = deltaTicks / (double)TimeSpan.TicksPerSecond;
        Assert.AreEqual(1.0, deltaSeconds, 1e-6,
            "One device-second of counter ticks should reconstruct to one second after a stop/start "
            + "raced the previous session's frequency apply.");
        Assert.AreEqual(1.0 / DEVICE_TIMESTAMP_FREQUENCY, _device.EffectiveTickPeriod, 1e-15,
            "The restarted session should have re-applied the device-reported frequency rather than "
            + "silently falling back to Core's 20 ns default.");
    }
    #endregion

    #region Helpers
    /// <summary>
    /// Starts a streaming session and routes its first frame, which a fresh connection holds until a
    /// successor validates the pair as same-session data, then arms the race. Nothing has reached
    /// <c>ProcessStreamMessage</c> yet, so no frequency has been applied.
    /// </summary>
    private void StartSessionAndArmTheRace()
    {
        _device.InitializeStreaming();
        _device.RouteStreamFrame(FIRST_FRAME_TIMESTAMP);
        _device.ArmStopStartDuringFrequencyApply();
    }

    /// <summary>
    /// Routes the successor frame, which releases the held pair and so performs the session's first
    /// frequency apply — firing the armed race — and returns once that race has finished.
    /// </summary>
    private void DriveTheRacedFrame()
    {
        _device.RouteStreamFrame(FIRST_FRAME_TIMESTAMP + SAMPLE_PERIOD_TICKS);
        _device.WaitForRacingStopStart();
    }
    #endregion

    #region Test Doubles
    /// <summary>
    /// A device whose timestamp processor can start a real stop/start from inside the frequency apply,
    /// so the ordering the serialization forbids is attempted on every run instead of being waited for.
    /// </summary>
    private sealed class InterleavingTestDevice : AbstractStreamingDevice, IDisposable
    {
        private const uint DEVICE_SERIAL_NUMBER = 12345;
        private const int ANALOG_PORT_COUNT = 1;

        private readonly SilentCoreDevice _coreDevice;
        private readonly RecordingTimestampProcessor _processor;
        private readonly ManualResetEventSlim _racingResetAtBoundary = new(false);
        private readonly ManualResetEventSlim _racingStopStartCompleted = new(false);
        private Thread? _racingStopStart;
        private Exception? _racingStopStartFailure;

        public InterleavingTestDevice()
        {
            _processor = new RecordingTimestampProcessor(new TimestampProcessor());

            // Both dependencies are init-only precisely so a test double can substitute an
            // instrumented one here; nothing can swap them afterwards.
            FrameTimestampProcessor = _processor;
            AppLogger = new BoundarySignallingAppLogger(_racingResetAtBoundary);

            _coreDevice = new SilentCoreDevice();
            _coreDevice.Connect();

            var statusMessage = new DaqifiOutMessage
            {
                DevicePn = "Nq1",
                DeviceSn = DEVICE_SERIAL_NUMBER,
                DeviceFwRev = "3.7.2",
                AnalogInPortNum = ANALOG_PORT_COUNT,
                AnalogInRes = 4095,
                TimestampFreq = DEVICE_TIMESTAMP_FREQUENCY,
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

        /// <summary>Calls the device made on its timestamp processor, in the order they happened.</summary>
        public List<string> ProcessorCalls => _processor.Calls;

        /// <summary>
        /// The tick period Core would use for this device's frames — the device-reported one when a
        /// frequency is applied, and the silent 20 ns fallback when it is not.
        /// </summary>
        public double EffectiveTickPeriod =>
            _processor.GetTickPeriod(DEVICE_SERIAL_NUMBER.ToString(CultureInfo.InvariantCulture));

        /// <summary>
        /// Whether the racing thread reached the statement immediately before <c>StopStreaming</c>'s
        /// guarded region while the transport thread still held that region open. Without this, a
        /// completion timeout would only prove the thread had not got going yet.
        /// </summary>
        public bool RacingResetReachedTheBoundary { get; private set; }

        /// <summary>
        /// Whether the racing reset ran to completion while the transport thread was still between the
        /// frequency apply and the gate write — the interleaving the fix has to make impossible.
        /// </summary>
        public bool RacingResetCompletedInsideTheGuardedRegion { get; private set; }

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
        /// Routes a frame through the desktop's inbound path — the handler Core invokes when it raises
        /// the classified <c>StreamMessageReceived</c> event. Core's own decode step is left out; these
        /// tests read the dispatched <see cref="DeviceMessage"/>s rather than channel samples.
        /// </summary>
        public void RouteStreamFrame(uint deviceTimestamp) => OnStreamMessageReceived(new DaqifiOutMessage
        {
            MsgTimeStamp = deviceTimestamp,
            DeviceSn = DEVICE_SERIAL_NUMBER,
            DeviceFwRev = "3.7.2",
            AnalogInDataFloat = { 1.25f }
        });

        /// <summary>
        /// Arms a one-shot race: the next frequency apply starts a real stop/start on another thread
        /// and does not return until that thread has reached the reset's critical-section boundary and
        /// then been watched for <see cref="RESET_PROBE_TIMEOUT_MS"/>.
        /// </summary>
        public void ArmStopStartDuringFrequencyApply()
        {
            _processor.OnFrequencyApplied = RaceStopStartAgainstThisFrame;
        }

        /// <summary>
        /// Waits for the armed stop/start to finish and surfaces anything it threw. Safe to call when
        /// nothing was armed.
        /// </summary>
        public void WaitForRacingStopStart()
        {
            var racingStopStart = _racingStopStart;
            if (racingStopStart == null)
            {
                return;
            }

            Assert.IsTrue(racingStopStart.Join(PROGRESS_TIMEOUT_MS),
                "The racing stop/start should complete once the guarded region is released.");
            if (_racingStopStartFailure != null)
            {
                Assert.Fail($"The racing stop/start threw: {_racingStopStartFailure}");
            }
        }

        /// <summary>
        /// Releases the fixture's Core connection, and the race handshake signals once the racing
        /// thread can no longer touch them.
        /// </summary>
        public void Dispose()
        {
            // Nothing the racing thread can still touch may be disposed until it has provably
            // exited. It signals both events, and its StopStreaming/InitializeStreaming run against
            // the Core device — so all three are on that thread's reachable set. If the join times
            // out, the test has already failed on that timeout; disposing here would then hand the
            // still-running worker an ObjectDisposedException on a background thread, masking the
            // real failure and destabilizing the test host. Leaking two ManualResetEventSlim
            // instances and one transport-less Core device on that path is by far the cheaper
            // outcome.
            var racingThreadExited = _racingStopStart?.Join(PROGRESS_TIMEOUT_MS) ?? true;
            if (racingThreadExited)
            {
                _racingResetAtBoundary.Dispose();
                _racingStopStartCompleted.Dispose();
                _coreDevice.Dispose();
            }
        }

        /// <summary>
        /// Runs on the transport thread, from inside the frequency apply. Starts the stop/start a user
        /// could trigger at this instant, waits until it is provably at the reset's critical-section
        /// boundary, and only then measures whether it can get past it before the gate write.
        /// </summary>
        private void RaceStopStartAgainstThisFrame()
        {
            _processor.OnFrequencyApplied = null;

            _racingStopStart = new Thread(() =>
            {
                try
                {
                    StopStreaming();
                    InitializeStreaming();
                }
                catch (Exception ex)
                {
                    _racingStopStartFailure = ex;
                }

                _racingStopStartCompleted.Set();
            })
            {
                IsBackground = true,
                Name = "Racing stop/start"
            };
            _racingStopStart.Start();

            // Signalled by the instrumented logger from StopStreaming's last statement before the
            // guarded region, so from here an unserialized reset has only the lock left to take.
            RacingResetReachedTheBoundary = _racingResetAtBoundary.Wait(PROGRESS_TIMEOUT_MS);
            RacingResetCompletedInsideTheGuardedRegion = _racingStopStartCompleted.Wait(RESET_PROBE_TIMEOUT_MS);
        }
    }

    /// <summary>
    /// Forwards every call to a real <see cref="TimestampProcessor"/>, recording the order they arrive
    /// in and exposing a one-shot hook that fires inside the frequency apply.
    /// </summary>
    private sealed class RecordingTimestampProcessor(ITimestampProcessor inner) : ITimestampProcessor
    {
        private readonly List<string> _calls = [];

        /// <summary>
        /// Invoked at the end of <see cref="SetTimestampFrequency"/>, on the calling thread and inside
        /// whatever region the caller is holding. Assigned and cleared from that same thread.
        /// </summary>
        public Action? OnFrequencyApplied { get; set; }

        public double TickPeriod => inner.TickPeriod;

        /// <summary>A snapshot of the recorded calls; the list itself is written from two threads.</summary>
        public List<string> Calls
        {
            get
            {
                lock (_calls)
                {
                    return [.. _calls];
                }
            }
        }

        public void SetTimestampFrequency(string deviceId, uint frequencyHz)
        {
            inner.SetTimestampFrequency(deviceId, frequencyHz);
            Record(SET_FREQUENCY_CALL);
            OnFrequencyApplied?.Invoke();
        }

        public double GetTickPeriod(string deviceId) => inner.GetTickPeriod(deviceId);

        public TimestampResult ProcessTimestamp(string deviceId, uint deviceTimestamp)
        {
            Record(PROCESS_TIMESTAMP_CALL);
            return inner.ProcessTimestamp(deviceId, deviceTimestamp);
        }

        public void Reset(string deviceId)
        {
            Record(nameof(Reset));
            inner.Reset(deviceId);
        }

        public void ResetAll()
        {
            Record(RESET_ALL_CALL);
            inner.ResetAll();
        }

        private void Record(string call)
        {
            lock (_calls)
            {
                _calls.Add(call);
            }
        }
    }

    /// <summary>
    /// Turns the one breadcrumb <c>StopStreaming</c> records immediately before its guarded region
    /// (<see cref="RESET_BOUNDARY_BREADCRUMB"/>) into the signal the race probe starts from.
    /// </summary>
    /// <remarks>
    /// Every other member is a deliberate no-op: the device under test logs routinely (sleep-state
    /// changes, leftover-frame discards) and none of it is what these tests observe. Swallowing it
    /// also keeps the suite from writing to the real NLog sink for diagnostics no one reads.
    /// </remarks>
    /// <param name="reachedResetBoundary">
    /// Set when the racing thread reaches the statement immediately before the guarded region.
    /// </param>
    private sealed class BoundarySignallingAppLogger(ManualResetEventSlim reachedResetBoundary) : IAppLogger
    {
        /// <summary>
        /// Signals <c>reachedResetBoundary</c> on the reset-boundary breadcrumb; ignores all others.
        /// </summary>
        /// <param name="category">Breadcrumb category. Unused — the message alone identifies the boundary.</param>
        /// <param name="message">Breadcrumb message, matched against <see cref="RESET_BOUNDARY_BREADCRUMB"/>.</param>
        /// <param name="level">Breadcrumb severity. Unused.</param>
        public void AddBreadcrumb(
            string category,
            string message,
            Common.Loggers.BreadcrumbLevel level = Common.Loggers.BreadcrumbLevel.Info)
        {
            if (message == RESET_BOUNDARY_BREADCRUMB)
            {
                reachedResetBoundary.Set();
            }
        }

        /// <summary>No-op. See the class remarks.</summary>
        /// <param name="message">Ignored.</param>
        public void Information(string message)
        {
        }

        /// <summary>No-op. See the class remarks.</summary>
        /// <param name="message">Ignored.</param>
        public void Warning(string message)
        {
        }

        /// <summary>No-op. See the class remarks.</summary>
        /// <param name="ex">Ignored.</param>
        /// <param name="message">Ignored.</param>
        public void Warning(Exception ex, string message)
        {
        }

        /// <summary>No-op. See the class remarks.</summary>
        /// <param name="message">Ignored.</param>
        public void Error(string message)
        {
        }

        /// <summary>No-op. See the class remarks.</summary>
        /// <param name="ex">Ignored.</param>
        /// <param name="message">Ignored.</param>
        public void Error(Exception ex, string message)
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
