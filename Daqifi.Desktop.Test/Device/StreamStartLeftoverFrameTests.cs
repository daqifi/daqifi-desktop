using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Device;
using Daqifi.Core.Communication.Messages;
using Daqifi.Core.Device;
using CoreStreamingDevice = Daqifi.Core.Device.DaqifiStreamingDevice;

namespace Daqifi.Desktop.Test.Device;

/// <summary>
/// Tests for leftover-frame rejection and timestamp-baseline reset at stream start (issue #573).
/// The device's free-running 32-bit counter is never reset between sessions, and the device
/// holds the final frame of a stopped session in its transmit path, emitting it as the first
/// frame of the next session. Without rejection, that frame anchors the new session's time
/// axis on prior-session data, shifting the plot by the stop-to-start gap (forward, or
/// backward across a counter wrap). Two guards are covered: reference-based rejection when a
/// counter value has been seen on this connection (restarts), and held-first-frame validation
/// when none has (first session after connect — the device's latched leftover survives a USB
/// disconnect/reconnect, see daqifi-nyquist-firmware#533).
/// </summary>
[TestClass]
public class StreamStartLeftoverFrameTests
{
    // Device counter runs at the 50 MHz default; one 100 Hz sample period is 500,000 ticks.
    private const uint SAMPLE_PERIOD_TICKS = 500_000;
    private const uint THIRTEEN_SECOND_GAP_TICKS = 650_000_000;

    private LeftoverFrameTestDevice _device;
    private AnalogChannel _channel;

    [TestInitialize]
    public void Setup()
    {
        _device = new LeftoverFrameTestDevice();
        _channel = new AnalogChannel(_device, BuildAnalogInputCoreChannel(0))
        {
            IsActive = true
        };
        _device.DataChannels.Add(_channel);
        _device.InitializeDeviceState();
    }

    [TestMethod]
    public void StreamRestart_LeftoverFrameFromPreviousSession_IsDiscarded()
    {
        // Arrange - first session establishes the counter reference, then stops
        _device.InitializeStreaming();
        _device.RouteStreamFrame(1_000_000_000);
        _device.RouteStreamFrame(1_000_000_000 + SAMPLE_PERIOD_TICKS);
        _device.StopStreaming();
        var dispatchedBeforeRestart = _device.DispatchedMessages.Count;

        // Act - restart; the device emits the held prior-session frame first (one sample
        // period after the last frame), then genuine frames offset by the 13 s gap
        _device.InitializeStreaming();
        var leftoverTimestamp = 1_000_000_000 + 2 * SAMPLE_PERIOD_TICKS;
        _device.RouteStreamFrame(leftoverTimestamp);
        var leftoverWasDiscarded = _channel.ActiveSample == null
            && _device.DispatchedMessages.Count == dispatchedBeforeRestart;

        _device.RouteStreamFrame(leftoverTimestamp + THIRTEEN_SECOND_GAP_TICKS);
        _device.RouteStreamFrame(leftoverTimestamp + THIRTEEN_SECOND_GAP_TICKS + SAMPLE_PERIOD_TICKS);

        // Assert
        Assert.IsTrue(leftoverWasDiscarded, "The leftover prior-session frame should not produce samples.");
        Assert.AreEqual(dispatchedBeforeRestart + 2, _device.DispatchedMessages.Count,
            "Both genuine frames should be processed.");

        var firstGenuine = _device.DispatchedMessages[^2];
        var secondGenuine = _device.DispatchedMessages[^1];
        var deltaSeconds = (secondGenuine.TimestampTicks - firstGenuine.TimestampTicks) / (double)TimeSpan.TicksPerSecond;
        Assert.IsTrue(deltaSeconds > 0 && deltaSeconds < 1.0,
            $"Consecutive genuine frames should be one sample period apart, not offset by the stop-to-start gap (was {deltaSeconds:F4}s).");
    }

    [TestMethod]
    public void StreamRestart_LeftoverFrameAcrossCounterWrap_IsDiscardedWithoutBackwardTime()
    {
        // Arrange - last frame of the first session sits just below the 32-bit wrap
        const uint nearWrapTimestamp = 4_294_800_000;
        _device.InitializeStreaming();
        _device.RouteStreamFrame(nearWrapTimestamp - SAMPLE_PERIOD_TICKS);
        _device.RouteStreamFrame(nearWrapTimestamp);
        _device.StopStreaming();
        var dispatchedBeforeRestart = _device.DispatchedMessages.Count;

        // Act - the leftover frame wraps the counter; genuine frames follow 13 s later
        _device.InitializeStreaming();
        var leftoverTimestamp = unchecked(nearWrapTimestamp + SAMPLE_PERIOD_TICKS);
        _device.RouteStreamFrame(leftoverTimestamp);
        var leftoverWasDiscarded = _device.DispatchedMessages.Count == dispatchedBeforeRestart;

        var firstGenuineTimestamp = unchecked(leftoverTimestamp + THIRTEEN_SECOND_GAP_TICKS);
        _device.RouteStreamFrame(firstGenuineTimestamp);
        _device.RouteStreamFrame(unchecked(firstGenuineTimestamp + SAMPLE_PERIOD_TICKS));

        // Assert - without rejection this scenario trips the false-positive rollover branch
        // and produces backward time on the axis
        Assert.IsTrue(leftoverWasDiscarded, "The wrapped leftover frame should not produce samples.");
        Assert.AreEqual(dispatchedBeforeRestart + 2, _device.DispatchedMessages.Count);

        var firstGenuine = _device.DispatchedMessages[^2];
        var secondGenuine = _device.DispatchedMessages[^1];
        Assert.IsTrue(secondGenuine.TimestampTicks > firstGenuine.TimestampTicks,
            "Time must move forward between genuine frames after a counter wrap at session start.");
    }

    [TestMethod]
    public void StreamRestart_TailFrameDroppedWhileStopped_StillCountsTowardReference()
    {
        // Arrange - device emits one final frame just after the stop command lands; it is
        // dropped by the streaming gate but must still advance the leftover reference
        _device.InitializeStreaming();
        _device.RouteStreamFrame(2_000_000_000);
        _device.StopStreaming();
        _device.RouteStreamFrame(2_000_000_000 + SAMPLE_PERIOD_TICKS);
        var dispatchedBeforeRestart = _device.DispatchedMessages.Count;

        // Act - the leftover at the next start follows the dropped tail frame, not the last
        // processed frame
        _device.InitializeStreaming();
        _device.RouteStreamFrame(2_000_000_000 + 2 * SAMPLE_PERIOD_TICKS);

        // Assert
        Assert.AreEqual(dispatchedBeforeRestart, _device.DispatchedMessages.Count,
            "A leftover frame one period after the dropped tail frame should be discarded.");
        Assert.IsNull(_channel.ActiveSample, "The discarded leftover frame should not update channel samples.");
    }

    [TestMethod]
    public void StreamRestart_RapidRestartWithinDetectionWindow_CapBoundsDiscardedFrames()
    {
        // Arrange - a stop-to-start gap shorter than the detection window makes genuine
        // frames look like leftovers; the cap bounds the loss
        _device.InitializeStreaming();
        _device.RouteStreamFrame(3_000_000_000);
        _device.StopStreaming();
        var dispatchedBeforeRestart = _device.DispatchedMessages.Count;

        // Act - route seven frames, each one sample period apart (all within the window)
        _device.InitializeStreaming();
        for (var i = 1; i <= 7; i++)
        {
            _device.RouteStreamFrame(3_000_000_000 + (uint)i * SAMPLE_PERIOD_TICKS);
        }

        // Assert - five discards maximum, then frames flow
        Assert.AreEqual(dispatchedBeforeRestart + 2, _device.DispatchedMessages.Count,
            "After the discard cap is reached, remaining frames should be processed.");
    }

    [TestMethod]
    public void FreshConnection_GenuineFirstFrames_AreEmittedInOrderAfterValidation()
    {
        // Arrange/Act - with no counter reference (first session after connect), the first frame
        // is held until the second frame's counter delta validates the pair as same-session data
        _device.InitializeStreaming();
        _device.RouteStreamFrame(123_456_789);
        var heldAfterFirstFrame = _device.DispatchedMessages.Count == 0 && _channel.ActiveSample == null;

        _device.RouteStreamFrame(123_456_789 + SAMPLE_PERIOD_TICKS);
        var releasedAfterSecondFrame = _device.DispatchedMessages.Count;

        _device.RouteStreamFrame(123_456_789 + 2 * SAMPLE_PERIOD_TICKS);

        // Assert - one-frame latency on the very first sample, then both released in order and
        // subsequent frames flow with no holding
        Assert.IsTrue(heldAfterFirstFrame, "The first frame of a fresh connection should be held for validation.");
        Assert.AreEqual(2, releasedAfterSecondFrame, "A consistent pair should release both frames.");
        Assert.AreEqual(3, _device.DispatchedMessages.Count, "Frames after validation should flow immediately.");

        var deltaSeconds = (_device.DispatchedMessages[1].TimestampTicks - _device.DispatchedMessages[0].TimestampTicks)
            / (double)TimeSpan.TicksPerSecond;
        Assert.IsTrue(deltaSeconds > 0 && deltaSeconds < 1.0,
            $"The released pair should be one sample period apart and in order (was {deltaSeconds:F4}s).");
    }

    [TestMethod]
    public void FreshConnection_StaleFirstFrame_IsDiscardedAndSessionAnchorsOnGenuineData()
    {
        // Arrange - the device's latched leftover survives a disconnect/reconnect, so the first
        // frame of a fresh connection can be prior-session data with no reference to flag it
        const uint staleTimestamp = 2_000_000_000;
        const uint genuineTimestamp = staleTimestamp + 1_200_000_000; // 24 s gap at 50 MHz

        // Act
        _device.InitializeStreaming();
        _device.RouteStreamFrame(staleTimestamp);
        _device.RouteStreamFrame(genuineTimestamp);
        var dispatchedAfterGenuineArrived = _device.DispatchedMessages.Count;
        _device.RouteStreamFrame(genuineTimestamp + SAMPLE_PERIOD_TICKS);

        // Assert - the stale frame is discarded when the genuine frame exposes the jump; the
        // genuine frame is re-held and released with its consistent successor
        Assert.AreEqual(0, dispatchedAfterGenuineArrived,
            "The stale held frame should be discarded (not dispatched) when the jump is detected.");
        Assert.AreEqual(2, _device.DispatchedMessages.Count, "Both genuine frames should be released.");

        var deltaSeconds = (_device.DispatchedMessages[1].TimestampTicks - _device.DispatchedMessages[0].TimestampTicks)
            / (double)TimeSpan.TicksPerSecond;
        Assert.IsTrue(deltaSeconds > 0 && deltaSeconds < 1.0,
            $"The session should anchor on the genuine pair, not span the stale gap (delta was {deltaSeconds:F4}s).");
    }

    [TestMethod]
    public void FreshConnection_StaleFirstFrameAcrossCounterWrap_IsDiscarded()
    {
        // Arrange - the counter wraps between the latched leftover and the genuine data; without
        // rejection this is the negative-time symptom on the first session after reconnect
        const uint staleTimestamp = 4_294_900_000;
        var genuineTimestamp = unchecked(staleTimestamp + 1_200_000_000); // wraps past 2^32

        // Act
        _device.InitializeStreaming();
        _device.RouteStreamFrame(staleTimestamp);
        _device.RouteStreamFrame(genuineTimestamp);
        _device.RouteStreamFrame(unchecked(genuineTimestamp + SAMPLE_PERIOD_TICKS));

        // Assert
        Assert.AreEqual(2, _device.DispatchedMessages.Count,
            "The wrapped stale frame should be discarded and the genuine pair released.");
        Assert.IsTrue(
            _device.DispatchedMessages[1].TimestampTicks > _device.DispatchedMessages[0].TimestampTicks,
            "Time must move forward between the released genuine frames.");
    }

    [TestMethod]
    public void FreshConnection_AllFramesMutuallyInconsistent_CapReleasesData()
    {
        // Arrange - pathological input: every consecutive pair implies a multi-second jump.
        // The discard cap must bound the loss and eventually release data.
        const uint thirteenSeconds = 650_000_000;

        // Act - seven frames, each 13 s of counter time apart
        _device.InitializeStreaming();
        for (var i = 0; i < 7; i++)
        {
            _device.RouteStreamFrame(1_000_000_000 + (uint)i * thirteenSeconds);
        }

        // Assert - five discards maximum, then the next pair is accepted as-is
        Assert.AreEqual(2, _device.DispatchedMessages.Count,
            "After the discard cap is reached, the held frame and its successor should be released.");
    }

    [TestMethod]
    public void StreamRestart_GenuineFirstFrameBeyondWindow_IsProcessedAsAnchor()
    {
        // Arrange
        _device.InitializeStreaming();
        _device.RouteStreamFrame(1_000_000_000);
        _device.StopStreaming();
        var dispatchedBeforeRestart = _device.DispatchedMessages.Count;

        // Act - device behaved (no leftover); first frame arrives offset by the full gap
        _device.InitializeStreaming();
        _device.RouteStreamFrame(1_000_000_000 + THIRTEEN_SECOND_GAP_TICKS);

        // Assert
        Assert.AreEqual(dispatchedBeforeRestart + 1, _device.DispatchedMessages.Count,
            "A genuine first frame beyond the detection window should be processed immediately.");
    }

    [TestMethod]
    public void InitializeStreaming_AfterStopThatSkippedReset_AnchorsNewSessionAtCurrentTime()
    {
        // Arrange - simulate an unplug-style stop where StopStreaming (and its timestamp
        // processor reset) never ran: streaming is flagged off without the stop sequence
        _device.InitializeStreaming();
        _device.RouteStreamFrame(1_000_000_000);
        _device.RouteStreamFrame(1_000_000_000 + SAMPLE_PERIOD_TICKS);
        _device.IsStreaming = false;
        var lastSessionTicks = _device.DispatchedMessages[^1].TimestampTicks;

        // Act - restart sixty counter-seconds later; without the reset on start, the stale
        // baseline would place the new session's samples a minute past the previous session
        const uint sixtySecondsTicks = 60u * 50_000_000;
        _device.InitializeStreaming();
        _device.RouteStreamFrame(1_000_000_000 + SAMPLE_PERIOD_TICKS + sixtySecondsTicks);

        // Assert - the new anchor sits at a fresh baseline near the previous session's last
        // sample (the test runs in milliseconds), not 60 s ahead via the stale counter baseline.
        // Both tick values come from the code under test, so no wall-clock read is needed. The
        // lower bound has slack because session 1's last sample sits one firmware period past
        // its own baseline, which can be slightly ahead of the new session's anchor.
        var anchorTicks = _device.DispatchedMessages[^1].TimestampTicks;
        var anchorOffsetSeconds = (anchorTicks - lastSessionTicks) / (double)TimeSpan.TicksPerSecond;
        Assert.IsTrue(anchorOffsetSeconds > -1 && anchorOffsetSeconds < 10,
            $"The new session should anchor at a fresh baseline, not the stale counter baseline (offset was {anchorOffsetSeconds:F1}s).");
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
    /// Test implementation that routes protobuf frames through the real inbound pipeline and
    /// records device-message dispatches instead of touching the LoggingManager singleton.
    /// </summary>
    private sealed class LeftoverFrameTestDevice : AbstractStreamingDevice
    {
        private readonly NoOpCoreStreamingDevice _coreDevice;

        public LeftoverFrameTestDevice()
        {
            _coreDevice = new NoOpCoreStreamingDevice();
            _coreDevice.Connect();
        }

        public List<DeviceMessage> DispatchedMessages { get; } = [];

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

        public void RouteStreamFrame(uint deviceTimestamp)
        {
            var message = new DaqifiOutMessage
            {
                MsgTimeStamp = deviceTimestamp,
                DeviceSn = 12345,
                DeviceFwRev = "1.0.0",
                AnalogInDataFloat = { 1.25f }
            };

            HandleInboundMessage(
                new MessageReceivedEventArgs(
                    new GenericInboundMessage<object>(message)));
        }
    }

    private sealed class NoOpCoreStreamingDevice() : CoreStreamingDevice("TestDevice")
    {
        public override void Send<T>(IOutboundMessage<T> message)
        {
        }
    }
}
