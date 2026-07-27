using Daqifi.Desktop.Device;
using Daqifi.Desktop.Logger;

namespace Daqifi.Desktop.Test.Loggers;

/// <summary>
/// Unit coverage for <see cref="SummaryLogger"/>'s device-level latency statistics — the
/// Min / Average / Max shown on the Summary flyout's "Latency" row (issue #760).
/// <para>
/// Latency is <c>AppTicks - TimestampTicks</c>: the gap between the instant the board stamped a
/// stream frame and the instant the app consumed it. A window is exactly <c>SampleSize</c> device
/// messages — <see cref="SummaryLogger.Log(DeviceMessage)"/> swaps the completed window in when its
/// message count reaches <c>SampleSize</c> — and the statistics are only readable through the public
/// properties once that swap has happened, so every test here pumps a whole number of windows.
/// </para>
/// <para>
/// These tests live in their own file rather than in <c>SummaryLoggerTests.cs</c> because that file
/// is added by the still-open PR #758; keeping them separate avoids an add/add merge conflict. They
/// can be folded together once both land.
/// </para>
/// </summary>
[TestClass]
public class SummaryLoggerLatencyTests
{
    private const string DEVICE_NAME = "Nq1";
    private const string DEVICE_SERIAL = "SN-1";

    /// <summary>
    /// The app-tick value the first message of a window is stamped with. Arbitrary, but large enough
    /// that a latency can be subtracted from it without going negative.
    /// </summary>
    private const long FIRST_APP_TICKS = 1_000_000L;

    /// <summary>
    /// Feeds one device message whose latency is exactly <paramref name="latencyTicks"/>, by stamping
    /// the device timestamp that far behind the app timestamp.
    /// </summary>
    private static void PumpMessage(SummaryLogger logger, long appTicks, long latencyTicks)
    {
        logger.Log(new DeviceMessage
        {
            DeviceName = DEVICE_NAME,
            DeviceSerialNo = DEVICE_SERIAL,
            AppTicks = appTicks,
            TimestampTicks = appTicks - latencyTicks
        });
    }

    /// <summary>
    /// Feeds one complete window of <c>SampleSize</c> messages carrying <paramref name="latencies"/>.
    /// The messages are spaced evenly in app time, which keeps the delta statistics uniform and lets
    /// each test vary latency alone.
    /// </summary>
    private static void PumpWindow(SummaryLogger logger, params long[] latencies)
    {
        Assert.AreEqual(
            logger.SampleSize,
            latencies.Length,
            "A window is exactly SampleSize messages; supply one latency per message.");

        var appTicks = FIRST_APP_TICKS;
        foreach (var latency in latencies)
        {
            PumpMessage(logger, appTicks, latency);
            appTicks += 10_000;
        }
    }

    /// <summary>
    /// Builds an array of <paramref name="count"/> copies of <paramref name="latency"/>.
    /// </summary>
    private static long[] Repeated(long latency, int count)
    {
        var values = new long[count];
        Array.Fill(values, latency);
        return values;
    }

    [TestMethod]
    public void Log_AverageLatency_IsTheMeanOfTheWindowsLatencies()
    {
        // Arrange
        var logger = new SummaryLogger(5);

        // Act
        PumpWindow(logger, 100, 200, 300, 400, 500);

        // Assert - (100 + 200 + 300 + 400 + 500) / 5. Dividing that sum by SampleSize - 1 gives 375.
        Assert.AreEqual(300.0, logger.AverageLatency, 1e-9);
    }

    /// <summary>
    /// The window bound must not scale the mean. Every message in both windows carries the same
    /// latency, so a correct mean is that latency at any <c>SampleSize</c>; a mean denominated by
    /// <c>SampleSize - 1</c> instead reads high by <c>SampleSize / (SampleSize - 1)</c>, which is a
    /// different number for each window size.
    /// </summary>
    [TestMethod]
    public void Log_AverageLatency_IsIndependentOfSampleSize()
    {
        // Arrange
        var small = new SummaryLogger(5);
        var large = new SummaryLogger(16);

        // Act - the same latency, measured through two different window bounds.
        PumpWindow(small, Repeated(1000, small.SampleSize));
        PumpWindow(large, Repeated(1000, large.SampleSize));

        // Assert
        Assert.AreEqual(1000.0, small.AverageLatency, 1e-9);
        Assert.AreEqual(1000.0, large.AverageLatency, 1e-9);
        Assert.AreEqual(small.AverageLatency, large.AverageLatency, 1e-9);
    }

    /// <summary>
    /// A single-message window is reachable from the UI — the Summary flyout's sample-size control is
    /// <c>&lt;controls:NumericUpDown ... Minimum="1" /&gt;</c> — and dividing that one latency by
    /// <c>SampleSize - 1</c> divides by zero, so the flyout displayed an infinity.
    /// </summary>
    [TestMethod]
    public void Log_AverageLatency_IsFinite_WhenSampleSizeIsOne()
    {
        // Arrange
        var logger = new SummaryLogger(1);

        // Act
        PumpWindow(logger, 700);

        // Assert
        Assert.IsFalse(double.IsInfinity(logger.AverageLatency), "Average latency must stay finite.");
        Assert.IsFalse(double.IsNaN(logger.AverageLatency), "Average latency must stay a number.");
        Assert.AreEqual(700.0, logger.AverageLatency, 1e-9);
    }

    /// <summary>
    /// Each window is scored on its own messages. <c>SummaryBuffer.Reset()</c> zeroes the accumulator
    /// on every swap, so a second window must not inherit any part of the first window's mean.
    /// </summary>
    [TestMethod]
    public void Log_AverageLatency_DoesNotCarryAcrossWindows()
    {
        // Arrange - complete one window whose mean is a known, non-zero value, so that anything
        // carried into the next window would show up rather than hide behind a zero. The check on
        // it is a precondition, not the assertion under test.
        var logger = new SummaryLogger(3);
        PumpWindow(logger, 100, 100, 100);
        Assert.AreEqual(
            100.0,
            logger.AverageLatency,
            1e-9,
            "Precondition: the first window must report its own mean before carry-over can be judged.");

        // Act - the single window under test.
        PumpWindow(logger, 400, 400, 400);

        // Assert
        Assert.AreEqual(400.0, logger.AverageLatency, 1e-9);
    }

    /// <summary>
    /// The extremes are seeded from the first message of the window rather than from the zeroed
    /// buffer, so a window whose latencies never reach zero still reports its own smallest and
    /// largest. This guards the seeding that the average accumulator was missing.
    /// </summary>
    [TestMethod]
    public void Log_MinAndMaxLatency_SpanEveryMessageInTheWindow()
    {
        // Arrange
        var logger = new SummaryLogger(4);

        // Act - the smallest and largest latencies sit in the middle of the window, so neither can
        // be reported by accident from the first or last message alone.
        PumpWindow(logger, 300, 100, 900, 500);

        // Assert
        Assert.AreEqual(100.0, logger.MinLatency, 1e-9);
        Assert.AreEqual(900.0, logger.MaxLatency, 1e-9);
    }

    /// <summary>
    /// Regression guard on the interval count the device-level <c>AverageDelta</c> is a mean over.
    /// It is guarded by <c>SampleCount &gt; 0</c>, so a window of <c>SampleSize</c> messages really
    /// does contribute <c>SampleSize - 1</c> intervals, and the mean must be over exactly those.
    /// The accumulator now reaches that answer incrementally rather than through a literal
    /// <c>SampleSize - 1</c> denominator (issue #771 - that denominator was only ever right while
    /// a window was guaranteed to be exactly <c>SampleSize</c> messages long), so this test pins
    /// the value the two forms agree on for a window nobody resized.
    /// </summary>
    [TestMethod]
    public void Log_AverageDelta_StillAveragesOverSampleSizeMinusOneIntervals()
    {
        // Arrange
        var logger = new SummaryLogger(5);
        var appTicks = FIRST_APP_TICKS;

        // Act - four intervals of 10, 20, 30 and 40 ticks between five messages: mean 25.
        PumpMessage(logger, appTicks, 1);
        foreach (var interval in new long[] { 10, 20, 30, 40 })
        {
            appTicks += interval;
            PumpMessage(logger, appTicks, 1);
        }

        // Assert
        Assert.AreEqual(25.0, logger.AverageDelta, 1e-9);
        Assert.AreEqual(10.0, logger.MinDelta, 1e-9);
        Assert.AreEqual(40.0, logger.MaxDelta, 1e-9);
    }

    /// <summary>
    /// A single-message window has no intervals at all, so the delta accumulator must never run and
    /// must not divide by its own zero denominator either.
    /// </summary>
    [TestMethod]
    public void Log_AverageDelta_IsZero_WhenSampleSizeIsOne()
    {
        // Arrange
        var logger = new SummaryLogger(1);

        // Act
        PumpWindow(logger, 700);

        // Assert
        Assert.AreEqual(0.0, logger.AverageDelta, 1e-9);
    }
}
