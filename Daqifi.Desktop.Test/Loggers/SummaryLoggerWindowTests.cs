using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Logger;
using ChannelType = Daqifi.Core.Channel.ChannelType;

namespace Daqifi.Desktop.Test.Loggers;

/// <summary>
/// Unit coverage for how <see cref="SummaryLogger"/> bounds a summary window, and for what happens
/// to a window in progress when the Summary flyout's sample size moves under it (issue #771).
/// <para>
/// A window is <c>SampleSize</c> device messages, and <see cref="SummaryLogger.Log(DeviceMessage)"/>
/// is the only path that publishes one: it swaps the completed buffer in and raises the change
/// notifications the flyout binds to. Nothing else in the class is observable until that happens, so
/// these tests use the <c>Channels</c> notification as the "a window completed" oracle and count how
/// many times it fires.
/// </para>
/// <para>
/// The pumping order matches production and the sibling fixtures: for each frame,
/// <c>AbstractStreamingDevice.ProcessStreamMessage</c> dispatches the <see cref="DeviceMessage"/>
/// first and Core raises that frame's channel samples immediately afterwards, so the message that
/// closes a window is swapped in before its own samples are logged.
/// </para>
/// </summary>
[TestClass]
public class SummaryLoggerWindowTests
{
    #region Constants
    private const string DEVICE_NAME = "Nq1";
    private const string DEVICE_SERIAL = "SN-1";

    /// <summary>App-tick stamp of the first frame; arbitrary, just far enough from zero to be real.</summary>
    private const long FIRST_APP_TICKS = 1_000_000L;

    /// <summary>Spacing between frames when a test does not care about the interval itself.</summary>
    private const long FRAME_INTERVAL_TICKS = 10_000L;

    /// <summary>The window length <c>Reset</c> restores; mirrors the logger's own default.</summary>
    private const int DEFAULT_SAMPLE_SIZE = 1000;

    /// <summary>
    /// Size the gate probe assigns. Any value the logger does not already hold will do - it exists
    /// only to make the probing thread travel the one public path that takes the gate.
    /// </summary>
    private const int PROBE_SAMPLE_SIZE = 9;

    /// <summary>
    /// How long the gate probe waits. Four orders of magnitude more than an uncontended lock
    /// needs, and only ever spent in full on a genuine failure.
    /// </summary>
    private const int GATE_PROBE_TIMEOUT_MS = 5_000;
    #endregion

    #region Fields
    private SummaryLogger _logger = null!;
    private int _windowsPublished;
    private int _lastNotifiedSampleSize;
    private long _nextAppTicks;
    #endregion

    #region Setup
    [TestInitialize]
    public void Setup()
    {
        _windowsPublished = 0;
        _lastNotifiedSampleSize = -1;
        _nextAppTicks = FIRST_APP_TICKS;
    }
    #endregion

    #region Tests
    [TestMethod]
    public void Log_SampleSizeLoweredMidWindow_PublishesACompletedWindowAgain()
    {
        // Arrange - a window in progress that has already run past the size it is about to be given
        NewLogger(sampleSize: 10);
        PumpFrames(5);
        Assert.AreEqual(0, _windowsPublished, "A window of 10 must not publish after only 5 messages.");

        // Act - the flyout's sample size drops below the count already reached
        _logger.SampleSize = 3;
        PumpFrames(3);

        // Assert - the summary keeps updating; a window bounded by equality alone could never
        // close again, freezing every field on the flyout for the rest of the session
        Assert.AreEqual(1, _windowsPublished,
            "Lowering the sample size mid-window must not stop windows from completing.");
    }

    [TestMethod]
    public void Log_SampleSizeLoweredMidWindow_StartsTheNewWindowFromScratch()
    {
        // Arrange - four frames' worth of samples accumulated under the old size
        NewLogger(sampleSize: 10);
        PumpFrames(4, "AI0");

        // Act - shrink the window, then complete one at the new size
        _logger.SampleSize = 3;
        PumpFrames(3, "AI0");

        // Assert - the published window holds only what arrived after the change: three messages,
        // the last of which swapped the window in before its own sample was logged. Carrying the
        // earlier accumulation over would report six samples averaged over a three-message window
        Assert.AreEqual(2, SummaryFor("AI0").SampleCount,
            "A resized window must start from scratch rather than inherit the previous accumulation.");
    }

    [TestMethod]
    public void Log_SampleSizeRaisedMidWindow_AlsoStartsTheNewWindowFromScratch()
    {
        // Arrange - a window in progress under a small size
        NewLogger(sampleSize: 3);
        PumpFrames(2);

        // Act - grow the window, then feed one full window's worth of messages at the new size
        _logger.SampleSize = 8;
        PumpFrames(7);
        Assert.AreEqual(0, _windowsPublished, "Seven messages must not complete a window of eight.");
        PumpFrames(1);

        // Assert - the count restarts on the change, so the eighth message after it is the one
        // that closes the window, not the sixth
        Assert.AreEqual(1, _windowsPublished, "A raised sample size measures its window from the change.");
    }

    [TestMethod]
    public void Log_SampleSizeLoweredToOneMidWindow_PublishesAFiniteAverageDelta()
    {
        // Arrange - a window whose interval accumulator has already been seeded, which is what
        // used to leave the (SampleSize - 1) denominator exposed to a size of 1
        NewLogger(sampleSize: 10);
        PumpFrames(5);

        // Act - the smallest size the flyout's NumericUpDown allows
        _logger.SampleSize = 1;
        PumpFrames(1);

        // Assert - a one-message window holds no interval at all, so its mean is zero rather than
        // the infinity a division by (1 - 1) produced
        Assert.AreEqual(1, _windowsPublished, "A sample size of 1 completes a window on the next message.");
        Assert.IsTrue(double.IsFinite(_logger.AverageDelta),
            $"AverageDelta must stay finite at a sample size of 1, but was {_logger.AverageDelta}.");
        Assert.AreEqual(0.0, _logger.AverageDelta, 1e-9, "A single-message window spans no interval.");
    }

    [TestMethod]
    public void Log_AverageDelta_IsTheMeanOfTheWindowsIntervals()
    {
        // Arrange - a window of three messages, so two intervals: 10,000 and 30,000 ticks
        NewLogger(sampleSize: 3);

        // Act
        PumpFrameAt(FIRST_APP_TICKS);
        PumpFrameAt(FIRST_APP_TICKS + 10_000);
        PumpFrameAt(FIRST_APP_TICKS + 40_000);

        // Assert - the incremental mean must agree with the sum-over-count it replaced
        Assert.AreEqual(1, _windowsPublished, "Three messages complete a window of three.");
        Assert.AreEqual(20_000.0, _logger.AverageDelta, 1e-9,
            "AverageDelta must be the mean of the intervals between the window's messages.");
    }

    [TestMethod]
    public void Log_UnchangedSampleSize_PublishesOneWindowPerSampleSizeMessages()
    {
        // Arrange - the ordinary case, where nothing touches the sample size
        NewLogger(sampleSize: 3);

        // Act
        PumpFrames(6);

        // Assert - relaxing the swap condition to >= must not change the cadence of a window that
        // is never resized
        Assert.AreEqual(2, _windowsPublished, "Six messages must complete exactly two windows of three.");
    }

    [TestMethod]
    public void SampleSize_SetToItsCurrentValue_DoesNotRestartTheWindow()
    {
        // Arrange - a window in progress, three of its five messages already banked
        NewLogger(sampleSize: 5);
        PumpFrames(3);

        // Act - the flyout re-assigns the size it already holds, which its NumericUpDown does on a
        // coerced or re-committed edit
        _logger.SampleSize = 5;
        PumpFrames(2);

        // Assert - restarting here would silently stretch the gap between summaries. Writing the
        // property by hand means keeping the equality guard [ObservableProperty] used to generate
        Assert.AreEqual(1, _windowsPublished,
            "Re-assigning the sample size it already has must not restart the window in progress.");
    }

    [TestMethod]
    public void SampleSize_WhenChanged_RaisesPropertyChangedOnlyOnAnActualChange()
    {
        // Arrange
        NewLogger(sampleSize: 5);
        var notifications = 0;
        _logger.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SummaryLogger.SampleSize))
            {
                notifications++;
            }
        };

        // Act
        _logger.SampleSize = 7;
        _logger.SampleSize = 7;

        // Assert - the flyout's NumericUpDown is two-way bound to this property, so a hand-written
        // setter that stopped notifying would leave the control and the logger permanently out of
        // step with each other
        Assert.AreEqual(7, _logger.SampleSize);
        Assert.AreEqual(1, notifications,
            "A changed sample size must notify exactly once, and an unchanged one not at all.");
    }

    [TestMethod]
    public void Log_PublishingAWindow_DoesNotHoldTheLockThatASampleSizeChangeNeeds()
    {
        // Arrange - a window that completes on its second message. Publishing raised its
        // notifications with the buffer lock still held, so a subscriber that had another thread
        // change the sample size could not get that thread into the setter until it returned - and
        // it is what is waiting on the thread
        NewLogger(sampleSize: 2);

        // Act - the second message closes the window and publishes it
        var gateWasFree = GateIsFreeWhileNotifying(nameof(SummaryLogger.Channels), () => PumpFrames(2));

        // Assert
        Assert.IsTrue(gateWasFree,
            "A completed window's notifications must be raised after the lock is released, or a "
            + "subscriber that reaches back into the logger deadlocks the streaming thread against "
            + "whichever thread is changing the sample size.");
    }

    [TestMethod]
    public void Reset_DoesNotHoldTheLockWhileNotifying()
    {
        // Arrange - a size other than the default, so resetting genuinely changes it and raises
        // the notification this probes on
        NewLogger(sampleSize: 5);

        // Act - Reset restores the default size while holding the gate. Assigning through the
        // SampleSize property notified from in there: a lock is re-entrant to its own thread, so
        // the setter leaving its own lock block did not release the gate Reset still owned
        var gateWasFree = GateIsFreeWhileNotifying(
            nameof(SummaryLogger.SampleSize), () => _logger.ResetCommand.Execute(null));

        // Assert
        Assert.IsTrue(gateWasFree,
            "Reset must release the gate before it notifies. Raising PropertyChanged underneath it "
            + "puts binding handlers on the lock the streaming thread needs, which is the deadlock "
            + "the publishing path is already written to avoid.");
    }

    [TestMethod]
    public void Stopping_DoesNotHoldTheLockWhileNotifying()
    {
        // Arrange - the constructor starts the logger, so the first toggle stops it
        NewLogger(sampleSize: 5);
        Assert.IsTrue(_logger.Enabled, "A logger built with a sample size starts enabled.");

        // Act
        var gateWasFree = GateIsFreeWhileNotifying(
            nameof(SummaryLogger.Enabled), () => _logger.ToggleEnabledCommand.Execute(null));

        // Assert
        Assert.IsTrue(gateWasFree,
            "Stopping must notify outside the gate, for the same reason publishing a window does.");
    }

    [TestMethod]
    public void Starting_DoesNotHoldTheLockWhileNotifying()
    {
        // Arrange - stopped, so the next toggle starts it and takes the gate to clear the window
        NewLogger(sampleSize: 5);
        _logger.ToggleEnabledCommand.Execute(null);
        Assert.IsFalse(_logger.Enabled, "The first toggle stops a logger that started enabled.");

        // Act
        var gateWasFree = GateIsFreeWhileNotifying(
            nameof(SummaryLogger.Enabled), () => _logger.ToggleEnabledCommand.Execute(null));

        // Assert
        Assert.IsTrue(gateWasFree,
            "Starting must notify outside the gate, for the same reason publishing a window does.");
    }

    [TestMethod]
    public void Reset_RestoresTheDefaultSampleSize_AndClearsTheSummaryOnScreen()
    {
        // Arrange - a published window on screen, under a size the flyout moved off the default
        NewLogger(sampleSize: 2);
        PumpFrames(2, "AI0");
        Assert.AreEqual(1, _windowsPublished, "Two messages complete a window of two.");

        // Act
        _logger.ResetCommand.Execute(null);

        // Assert - the default has to come back through a notification as well as into the field,
        // because the flyout's control is two-way bound and would otherwise keep showing the old
        // size while the logger measured windows against the new one
        Assert.AreEqual(DEFAULT_SAMPLE_SIZE, _logger.SampleSize, "Reset restores the default window length.");
        Assert.AreEqual(DEFAULT_SAMPLE_SIZE, _lastNotifiedSampleSize,
            "Reset must notify that the sample size changed, not just change it.");
        Assert.IsFalse(_logger.Enabled, "Reset stops the logger.");

        // The published window is cleared in place: SummaryBuffer.Reset zeroes each channel's
        // stats but keeps its entry, so the flyout's rows stay put and read zero rather than
        // vanishing under the user
        Assert.AreEqual(0, SummaryFor("AI0").SampleCount, "Reset clears the published window too.");
        Assert.AreEqual(0.0, _logger.ElapsedTime, 1e-9, "Reset clears the published window's timing.");
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(int.MinValue)]
    public void SampleSize_SetBelowOne_IsClampedToTheSmallestRealWindow(int offered)
    {
        // Arrange
        NewLogger(sampleSize: 5);

        // Act
        _logger.SampleSize = offered;

        // Assert - a window of one message is legal and the flyout's control allows it; zero or
        // fewer is not a window at all, and the property is public
        Assert.AreEqual(1, _logger.SampleSize,
            $"A sample size of {offered} must be clamped to the flyout's own minimum of 1.");
    }

    [TestMethod]
    public void SampleSize_CoercedToTheSizeItAlreadyHolds_StillNotifies()
    {
        // Arrange - already at the minimum, so clamping an offered 0 lands on the stored value
        NewLogger(sampleSize: 1);
        var notifications = 0;
        _logger.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SummaryLogger.SampleSize))
            {
                notifications++;
            }
        };

        // Act
        _logger.SampleSize = 0;

        // Assert - the stored size did not move, so the equality guard alone would notify nothing
        // and the two-way bound control would sit on 0 forever. A refused value still has to be
        // answered, or the control and the logger disagree with no way for the user to tell
        Assert.AreEqual(1, _logger.SampleSize);
        Assert.AreEqual(1, notifications,
            "A coerced assignment must notify so the control re-reads the size actually held.");
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void Constructor_SampleSizeBelowOne_IsClampedToTheSmallestRealWindow(int offered)
    {
        // Act - the constructor assigns the backing field directly, bypassing the setter's clamp
        var logger = new SummaryLogger(offered);

        // Assert
        Assert.AreEqual(1, logger.SampleSize,
            $"A logger constructed at {offered} must start at the same minimum the property enforces.");
    }
    #endregion

    #region Test Helpers
    /// <summary>
    /// Builds the logger under test and subscribes the window-completion oracle. Subscribing here
    /// rather than in <c>Setup</c> keeps the constructor's own notifications out of the count.
    /// </summary>
    private void NewLogger(int sampleSize)
    {
        _logger = new SummaryLogger(sampleSize);
        _logger.PropertyChanged += OnLoggerPropertyChanged;
    }

    /// <summary>
    /// Counts completed windows and records the size reported by the last sample-size notification.
    /// <c>SwapBuffer</c> is the only thing that raises <see cref="SummaryLogger.Channels"/>, so one
    /// notification is one published window.
    /// </summary>
    private void OnLoggerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SummaryLogger.Channels):
                _windowsPublished++;
                break;
            case nameof(SummaryLogger.SampleSize):
                _lastNotifiedSampleSize = _logger.SampleSize;
                break;
        }
    }

    /// <summary>
    /// Runs <paramref name="act"/> and, the first time the logger raises
    /// <paramref name="propertyName"/> while it does, sends a separate thread down the one public
    /// path that takes the logger's gate - assigning <see cref="SummaryLogger.SampleSize"/>.
    /// Reports whether that thread got through within <see cref="GATE_PROBE_TIMEOUT_MS"/>.
    /// </summary>
    /// <remarks>
    /// This is what a real subscriber that reaches back into the logger does, and it is the shape
    /// of the deadlock the class is written to avoid: if the notification is raised with the gate
    /// held, the probing thread parks in the setter until the handler returns - and the handler is
    /// what is waiting on the probing thread.
    /// <para>
    /// A dedicated <see cref="Thread"/> rather than the pool, so the wait measures the lock and not
    /// the thread pool's willingness to start the work item. The probe runs once: its own
    /// assignment raises <see cref="SummaryLogger.SampleSize"/>, which would otherwise re-enter.
    /// </para>
    /// </remarks>
    private bool GateIsFreeWhileNotifying(string propertyName, Action act)
    {
        var gateWasFree = false;
        var probed = false;

        void Probe(object? sender, PropertyChangedEventArgs e)
        {
            if (probed || e.PropertyName != propertyName)
            {
                return;
            }

            probed = true;
            var contender = new Thread(() => _logger.SampleSize = PROBE_SAMPLE_SIZE) { IsBackground = true };
            contender.Start();
            gateWasFree = contender.Join(GATE_PROBE_TIMEOUT_MS);
        }

        _logger.PropertyChanged += Probe;
        try
        {
            act();
        }
        finally
        {
            _logger.PropertyChanged -= Probe;
        }

        Assert.IsTrue(probed,
            $"The act never raised {propertyName}, so the gate was never probed and the result "
            + "would be meaningless.");
        return gateWasFree;
    }

    /// <summary>
    /// Feeds <paramref name="count"/> evenly spaced frames, each optionally carrying one sample per
    /// name in <paramref name="channelNames"/>.
    /// </summary>
    private void PumpFrames(int count, params string[] channelNames)
    {
        for (var i = 0; i < count; i++)
        {
            PumpFrameAt(_nextAppTicks, channelNames);
            _nextAppTicks += FRAME_INTERVAL_TICKS;
        }
    }

    /// <summary>
    /// Feeds one frame stamped at <paramref name="appTicks"/>: the frame's device message followed
    /// by its channel samples, matching the production dispatch order described on the class.
    /// </summary>
    private void PumpFrameAt(long appTicks, params string[] channelNames)
    {
        _logger.Log(new DeviceMessage
        {
            DeviceName = DEVICE_NAME,
            DeviceSerialNo = DEVICE_SERIAL,
            TimestampTicks = appTicks,
            AppTicks = appTicks
        });

        foreach (var channelName in channelNames)
        {
            _logger.Log(new DataSample
            {
                DeviceName = DEVICE_NAME,
                DeviceSerialNo = DEVICE_SERIAL,
                ChannelName = channelName,
                Type = ChannelType.Analog,
                Color = "#FF0000FF",
                Value = 1.0,
                TimestampTicks = appTicks
            });
        }
    }

    private SummaryLogger.ChannelSummary SummaryFor(string channelName)
        => _logger.Channels.Single(c => c.Name == channelName);
    #endregion
}
