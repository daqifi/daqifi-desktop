using System.ComponentModel;
using System.Linq;
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
    #endregion

    #region Fields
    private SummaryLogger _logger = null!;
    private int _windowsPublished;
    private long _nextAppTicks;
    #endregion

    #region Setup
    [TestInitialize]
    public void Setup()
    {
        _windowsPublished = 0;
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
    /// Counts completed windows. <c>SwapBuffer</c> is the only thing that raises
    /// <see cref="SummaryLogger.Channels"/>, so one notification is one published window.
    /// </summary>
    private void OnLoggerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SummaryLogger.Channels))
        {
            _windowsPublished++;
        }
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
