using System.Linq;
using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Logger;
using ChannelType = Daqifi.Core.Channel.ChannelType;

namespace Daqifi.Desktop.Test.Loggers;

/// <summary>
/// Unit coverage for <see cref="SummaryLogger"/>'s per-channel value statistics — the
/// Min / Average / Max shown on the Summary flyout's "Value" row (issue #757).
/// <para>
/// The tests drive the logger in the same order production does: for each stream frame,
/// <c>AbstractStreamingDevice.ProcessStreamMessage</c> dispatches the frame's
/// <see cref="DeviceMessage"/> and Core then raises that frame's channel samples immediately
/// afterwards, so the message is always logged before its samples. Because
/// <see cref="SummaryLogger.Log(DeviceMessage)"/> is what swaps the completed window into the
/// buffer <c>Channels</c> reads from, a window of N per-channel samples takes N+1 frames: the
/// (N+1)-th message triggers the swap before its own samples are logged, and those samples
/// start the next window.
/// </para>
/// </summary>
[TestClass]
public class SummaryLoggerTests
{
    private const string DeviceSerial = "SN-1";

    /// <summary>
    /// Feeds one stream frame: the frame's device message followed by its channel samples,
    /// matching the production dispatch order described on the class.
    /// </summary>
    private static void PumpFrame(
        SummaryLogger logger,
        long timestampTicks,
        params (string Channel, double Value)[] samples)
    {
        logger.Log(new DeviceMessage
        {
            DeviceName = "Nq1",
            DeviceSerialNo = DeviceSerial,
            TimestampTicks = timestampTicks,
            AppTicks = timestampTicks
        });

        foreach (var (channel, value) in samples)
        {
            logger.Log(new DataSample
            {
                DeviceName = "Nq1",
                DeviceSerialNo = DeviceSerial,
                ChannelName = channel,
                Type = ChannelType.Analog,
                Color = "#FF0000FF",
                Value = value,
                TimestampTicks = timestampTicks
            });
        }
    }

    /// <summary>
    /// Feeds one complete window: <paramref name="values"/> for a single channel, then enough
    /// sample-less frames to take the window's device-message count up to <c>SampleSize</c>,
    /// which is what swaps the window in and makes it readable through <c>Channels</c>.
    /// </summary>
    private static void PumpWindow(SummaryLogger logger, string channelName, params double[] values)
    {
        var ticks = 1_000_000L;
        foreach (var value in values)
        {
            PumpFrame(logger, ticks, (channelName, value));
            ticks += 10_000;
        }

        for (var i = values.Length; i < logger.SampleSize; i++)
        {
            PumpFrame(logger, ticks);
            ticks += 10_000;
        }
    }

    /// <summary>
    /// Feeds one complete window for a single channel whose samples are spaced by
    /// <paramref name="intervalTicks"/>, then pads the window out to <c>SampleSize</c> device
    /// messages so it swaps in. A run of N intervals produces N + 1 samples on the channel, and the
    /// padding frames carry no samples, so they add no intervals of their own.
    /// </summary>
    private static void PumpIntervalWindow(SummaryLogger logger, string channelName, params long[] intervalTicks)
    {
        var ticks = 1_000_000L;
        PumpFrame(logger, ticks, (channelName, 1.0));

        var frames = 1;
        foreach (var interval in intervalTicks)
        {
            ticks += interval;
            PumpFrame(logger, ticks, (channelName, 1.0));
            frames++;
        }

        for (; frames < logger.SampleSize; frames++)
        {
            ticks += 10_000;
            PumpFrame(logger, ticks);
        }
    }

    private static SummaryLogger.ChannelSummary SummaryFor(SummaryLogger logger, string channelName)
        => logger.Channels.Single(c => c.Name == channelName);

    [TestMethod]
    public void Log_PositiveOnlyChannel_ReportsSmallestSampleAsMinValue()
    {
        // Arrange: a window whose samples never reach zero. Every value is a valid minimum
        // candidate, so an unseeded running minimum shows up as a hard 0.
        var logger = new SummaryLogger(sampleSize: 4);

        // Act
        PumpWindow(logger, "AI0", 2.5, 4.0, 3.25);

        // Assert
        Assert.AreEqual(2.5, SummaryFor(logger, "AI0").MinValue, 1e-9,
            "MinValue must be the smallest sample in the window, not the 0 the buffer starts at.");
    }

    [TestMethod]
    public void Log_NegativeOnlyChannel_ReportsLargestSampleAsMaxValue()
    {
        // Arrange: the mirror case — every sample is below zero, so an unseeded running
        // maximum stays pinned at 0.
        var logger = new SummaryLogger(sampleSize: 4);

        // Act
        PumpWindow(logger, "AI0", -5.0, -1.5, -3.0);

        // Assert
        Assert.AreEqual(-1.5, SummaryFor(logger, "AI0").MaxValue, 1e-9,
            "MaxValue must be the largest sample in the window, not the 0 the buffer starts at.");
    }

    [TestMethod]
    public void Log_SingleSampleWindow_ReportsThatSampleAsBothMinAndMax()
    {
        // Arrange: with exactly one sample the seeding branch is the only one that runs, so
        // this isolates it from the Math.Min/Math.Max accumulation path entirely.
        var logger = new SummaryLogger(sampleSize: 2);

        // Act
        PumpWindow(logger, "AI0", 4.2);

        // Assert
        var summary = SummaryFor(logger, "AI0");
        Assert.AreEqual(1, summary.SampleCount, "The completed window should hold exactly one sample.");
        Assert.AreEqual(4.2, summary.MinValue, 1e-9, "A one-sample window's minimum is that sample.");
        Assert.AreEqual(4.2, summary.MaxValue, 1e-9, "A one-sample window's maximum is that sample.");
    }

    [TestMethod]
    public void Log_MinAndMax_SpanEverySampleIncludingTheFirst()
    {
        // Arrange: the extremes are deliberately placed on the first and last samples, so a
        // window that skipped its first sample would report 3.0 as the minimum.
        var logger = new SummaryLogger(sampleSize: 5);

        // Act
        PumpWindow(logger, "AI0", 1.0, 3.0, 5.0, 9.0);

        // Assert
        var summary = SummaryFor(logger, "AI0");
        Assert.AreEqual(1.0, summary.MinValue, 1e-9, "The first sample must participate in the minimum.");
        Assert.AreEqual(9.0, summary.MaxValue, 1e-9, "The last sample must participate in the maximum.");
    }

    [TestMethod]
    public void Log_AverageValue_IsMeanOfTheSamplesTheChannelReceived()
    {
        // Arrange: SampleSize counts device messages, and a channel's window is swapped out one
        // sample short of it, so an average divided by SampleSize reads low (here 9/4 = 2.25
        // instead of 3.0).
        var logger = new SummaryLogger(sampleSize: 4);

        // Act
        PumpWindow(logger, "AI0", 1.0, 2.0, 6.0);

        // Assert
        var summary = SummaryFor(logger, "AI0");
        Assert.AreEqual(3, summary.SampleCount, "The completed window should hold three samples.");
        Assert.AreEqual(3.0, summary.AverageValue, 1e-9,
            "AverageValue must be the mean of the samples the channel actually received.");
    }

    [TestMethod]
    public void Log_AverageValue_IsIndependentOfSampleSize()
    {
        // Arrange: the same three samples, read back through two different window sizes. The
        // mean of a channel's samples cannot depend on how many device messages bound the
        // window — SampleSize is a live, user-editable field on the flyout.
        var small = new SummaryLogger(sampleSize: 4);
        var large = new SummaryLogger(sampleSize: 16);

        // Act
        PumpWindow(small, "AI0", 1.0, 2.0, 6.0);
        PumpWindow(large, "AI0", 1.0, 2.0, 6.0);

        // Assert
        Assert.AreEqual(
            SummaryFor(small, "AI0").AverageValue,
            SummaryFor(large, "AI0").AverageValue,
            1e-9,
            "The reported mean must not scale with SampleSize.");
    }

    [TestMethod]
    public void Log_AverageDelta_IsMeanOfTheChannelsOwnIntervals()
    {
        // Arrange: four samples on one channel, spaced unevenly, so the three intervals average
        // 20,000 ticks. Dividing their sum by (SampleSize - 1) = 4 instead reports 15,000 — the
        // same message-window-vs-per-channel-count mismatch that skewed AverageValue.
        var logger = new SummaryLogger(sampleSize: 5);

        // Act
        PumpIntervalWindow(logger, "AI0", 10_000, 30_000, 20_000);

        // Assert
        var summary = SummaryFor(logger, "AI0");
        Assert.AreEqual(4, summary.SampleCount, "The completed window should hold four samples.");
        Assert.AreEqual(20_000.0, summary.AverageDelta, 1e-9,
            "AverageDelta must be the mean of the intervals between the channel's own samples.");
    }

    [TestMethod]
    public void Log_AverageDelta_IsIndependentOfSampleSize()
    {
        // Arrange: identical sample spacing read back through two window sizes. How many device
        // messages bound the window cannot change how far apart a channel's samples were.
        var small = new SummaryLogger(sampleSize: 5);
        var large = new SummaryLogger(sampleSize: 16);

        // Act
        PumpIntervalWindow(small, "AI0", 10_000, 30_000, 20_000);
        PumpIntervalWindow(large, "AI0", 10_000, 30_000, 20_000);

        // Assert
        Assert.AreEqual(
            SummaryFor(small, "AI0").AverageDelta,
            SummaryFor(large, "AI0").AverageDelta,
            1e-9,
            "The reported mean interval must not scale with SampleSize.");
    }

    [TestMethod]
    public void Log_MultipleChannels_TrackMinAndMaxIndependently()
    {
        // Arrange: one all-positive channel and one all-negative channel in the same window —
        // the two failure directions of an unseeded extreme, side by side.
        var logger = new SummaryLogger(sampleSize: 3);
        var ticks = 1_000_000L;

        // Act
        PumpFrame(logger, ticks, ("AI0", 2.0), ("AI1", -4.0));
        PumpFrame(logger, ticks + 10_000, ("AI0", 3.0), ("AI1", -2.0));
        PumpFrame(logger, ticks + 20_000);

        // Assert
        var analogZero = SummaryFor(logger, "AI0");
        var analogOne = SummaryFor(logger, "AI1");
        Assert.AreEqual(2.0, analogZero.MinValue, 1e-9, "AI0's minimum is its own smallest sample.");
        Assert.AreEqual(3.0, analogZero.MaxValue, 1e-9, "AI0's maximum is its own largest sample.");
        Assert.AreEqual(-4.0, analogOne.MinValue, 1e-9, "AI1's minimum is its own smallest sample.");
        Assert.AreEqual(-2.0, analogOne.MaxValue, 1e-9, "AI1's maximum is its own largest sample.");
    }

    [TestMethod]
    public void Log_SecondWindow_ReseedsMinAndMaxFromItsOwnFirstSample()
    {
        // Arrange: buffers are recycled across swaps (ChannelBuffer.Reset zeroes the extremes
        // but the channel entry survives), so a later window must re-seed rather than inherit
        // either the previous window's extremes or the reset 0.
        var logger = new SummaryLogger(sampleSize: 3);

        // Act
        PumpWindow(logger, "AI0", 10.0, 11.0);
        PumpWindow(logger, "AI0", 2.0, 3.0);

        // Assert
        var summary = SummaryFor(logger, "AI0");
        Assert.AreEqual(2.0, summary.MinValue, 1e-9, "The second window's minimum comes from its own samples.");
        Assert.AreEqual(3.0, summary.MaxValue, 1e-9, "The second window's maximum comes from its own samples.");
    }
}
