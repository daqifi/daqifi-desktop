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
/// The interleave is driven deterministically rather than by a stress loop: the fixture substitutes an
/// instrumented <see cref="ITimestampProcessor"/> whose <c>SetTimestampFrequency</c> starts a real
/// stop/start on another thread and gives it a window to land, which is exactly the sequence the
/// serialization has to make impossible.
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
    /// How long the racing stop/start is given to complete while the transport thread is still inside
    /// its guarded region. Long enough that an unserialized reset — a handful of field writes and two
    /// no-op Core sends — would comfortably finish; bounded so the serialized case cannot deadlock.
    /// </summary>
    private const int RESET_PROBE_TIMEOUT_MS = 250;

    /// <summary>Generous ceiling on waits that must succeed, so a hang fails as an assertion.</summary>
    private const int PROGRESS_TIMEOUT_MS = 30_000;

    private const string SET_FREQUENCY_CALL = "SetTimestampFrequency";
    private const string PROCESS_TIMESTAMP_CALL = "ProcessTimestamp";
    private const string RESET_ALL_CALL = "ResetAll";
    #endregion

    #region Fields
    private InterleavingTestDevice _device = null!;
    #endregion

    #region Setup and Teardown
    [TestInitialize]
    public void Setup()
    {
        _device = new InterleavingTestDevice();
    }

    // MSTest disposes the test-class instance after each test, releasing the device's Core connection
    // instead of leaking one per test (CA1001).
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
        // Arrange & Act
        RunFrequencyApplyRacedByStopStart();

        // Assert — the racing stop/start was already running and trying to reset while the transport
        // thread held the frame open, so if it completed there, the gate write that follows would have
        // latched "applied" over a frequency the reset had just dropped.
        Assert.IsTrue(_device.RacingStopStartWasRunning,
            "Precondition: the racing stop/start should have started before the guarded region ended.");
        Assert.IsFalse(_device.RacingStopStartCompletedInsideTheGuardedRegion,
            "A stream stop/start must not be able to reset the timestamp processor while a frame is "
            + "between its frequency apply and the gate write that records it.");
    }

    [TestMethod]
    public void ResetRacingTheFrequencyApply_CannotSplitTheApplyFromTheReconstructionItWasAppliedFor()
    {
        // Arrange & Act
        RunFrequencyApplyRacedByStopStart();

        // Assert — a reset between the apply and this frame's reconstruction would silently revert the
        // frame to the 50 MHz fallback tick and re-baseline it, so the apply and the ProcessTimestamp
        // it was performed for have to stay adjacent.
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
        RunFrequencyApplyRacedByStopStart();

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
    /// Runs one streaming session whose first frequency apply is raced by a real stop/start on another
    /// thread, and returns once that stop/start has finished. A fresh connection holds the first frame
    /// until its successor validates the pair as same-session data, so the apply — and with it the
    /// armed race — happens inside the second routed frame.
    /// </summary>
    private void RunFrequencyApplyRacedByStopStart()
    {
        _device.InitializeStreaming();
        _device.RouteStreamFrame(FIRST_FRAME_TIMESTAMP);
        _device.ArmStopStartDuringFrequencyApply();
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
        private readonly ManualResetEventSlim _racingStopStartStarted = new(false);
        private readonly ManualResetEventSlim _racingStopStartCompleted = new(false);
        private Thread? _racingStopStart;
        private Exception? _racingStopStartFailure;

        public InterleavingTestDevice()
        {
            _processor = new RecordingTimestampProcessor(new TimestampProcessor());

            // The production processor is init-only precisely so a test double can substitute an
            // instrumented one here; nothing can swap it afterwards.
            FrameTimestampProcessor = _processor;

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
            _processor.GetTickPeriod(DEVICE_SERIAL_NUMBER.ToString(System.Globalization.CultureInfo.InvariantCulture));

        /// <summary>Whether the racing stop/start had actually begun before the guarded region ended.</summary>
        public bool RacingStopStartWasRunning { get; private set; }

        /// <summary>
        /// Whether the racing stop/start ran to completion while the transport thread was still between
        /// the frequency apply and the gate write — the interleaving the fix has to make impossible.
        /// </summary>
        public bool RacingStopStartCompletedInsideTheGuardedRegion { get; private set; }

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
        /// and does not return until that thread is running and has been given
        /// <see cref="RESET_PROBE_TIMEOUT_MS"/> to complete.
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

        public void Dispose()
        {
            _racingStopStart?.Join(PROGRESS_TIMEOUT_MS);
            _racingStopStartStarted.Dispose();
            _racingStopStartCompleted.Dispose();
            _coreDevice.Dispose();
        }

        /// <summary>
        /// Runs on the transport thread, from inside the frequency apply. Starts the stop/start a user
        /// could trigger at this instant and gives it a real window to land before the gate write that
        /// follows.
        /// </summary>
        private void RaceStopStartAgainstThisFrame()
        {
            _processor.OnFrequencyApplied = null;

            _racingStopStart = new Thread(() =>
            {
                _racingStopStartStarted.Set();
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

            RacingStopStartWasRunning = _racingStopStartStarted.Wait(PROGRESS_TIMEOUT_MS);
            RacingStopStartCompletedInsideTheGuardedRegion = _racingStopStartCompleted.Wait(RESET_PROBE_TIMEOUT_MS);
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

    /// <summary>A transport-less Core streaming device: commands are accepted and dropped.</summary>
    private sealed class SilentCoreDevice() : CoreStreamingDevice("TestDevice")
    {
        public override void Send<T>(IOutboundMessage<T> message)
        {
        }
    }
    #endregion
}
