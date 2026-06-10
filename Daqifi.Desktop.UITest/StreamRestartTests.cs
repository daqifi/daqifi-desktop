using System;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daqifi.Desktop.UITest;

/// <summary>
/// Scenario — restart streaming after a stop gap and assert the <b>time axis anchors on the new
/// session's own data</b> (issue #573). The device's hardware timestamp is a free-running 32-bit
/// counter that is never reset, and the device holds the final frame of a stopped session in its
/// transmit path, emitting it as the first frame of the next session. Without leftover-frame
/// rejection, that stale frame anchors the new session's time baseline, so the real data lands at
/// +gap seconds on the axis — or at large negative time when the counter wrapped during the gap
/// and Core's rollover sanity check emits a backward delta. The assertions read the Live Graph
/// pane's plot-stats hook (<c>PlotStatsText</c>, extended with <c>firstx</c>/<c>lastx</c>):
/// after the restart, the axis must start at a non-negative X near zero and span only the time
/// actually streamed — not the stop-to-start gap. Self-cleans the two sessions it creates.
/// Requires a DAQiFi device (USB or WiFi).
/// </summary>
[TestClass]
public class StreamRestartTests : DaqifiAppFixture
{
    #region Constants
    // Matches the other streaming scenarios: gentle enough for out-of-process automation.
    private const double TARGET_FREQUENCY_HZ = 100d;

    // The stop-to-start gap is the TEST STIMULUS, not a readiness wait: the leftover frame's
    // counter offset from the new session equals this gap, which is exactly what mis-anchors
    // the axis on unfixed builds. It must comfortably exceed the app's leftover detection
    // window (2.5 s) and the axis-anchor assertion bound below.
    private static readonly TimeSpan StopStartGap = TimeSpan.FromSeconds(8);

    // The new session's first rendered sample must sit this close to X = 0 ms. A session
    // anchored on a leftover frame puts the real data at ~StopStartGap instead.
    private const double MAX_ANCHOR_OFFSET_MS = 2500d;

    // Upper bounds for polling waits (return as soon as satisfied).
    private static readonly TimeSpan PlotGrowthTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(15);
    #endregion

    [TestMethod]
    [TestCategory("Ui")]
    [TestCategory("RequiresDevice")]
    public void RestartStreamingAfterGap_TimeAxisAnchorsOnNewSession()
    {
        // Arrange — connect, record the session-count baseline, configure channels/frequency.
        var transport = ResolveTransport();
        ConnectFirstDevice(transport);

        var sessionsBefore = GetLoggedSessionCount();

        SetSamplingFrequency(TARGET_FREQUENCY_HZ);
        var activeChannels = EnableAllAnalogChannels();
        Assert.IsTrue(
            activeChannels > 0,
            "Pre-condition failed: no analog channels became active.");

        // Session 1 — run briefly so the device is left holding a leftover frame at stop.
        StartLogging();
        WaitForLoggingStatusLabel("LOGGING ON", StatusTimeout);
        WaitForPlotPointGrowth(PlotGrowthTimeout);
        StopLogging();
        WaitForLoggingStatusLabel("LOGGING OFF", StatusTimeout);

        // The deliberate stop-to-start gap (see StopStartGap above). A fixed sleep is correct
        // here: nothing is being waited FOR — the elapsed time itself creates the counter
        // offset that reproduces the bug.
        Thread.Sleep(StopStartGap);

        // Act — session 2: restart streaming and let the plot accrue real data.
        StartLogging();
        WaitForLoggingStatusLabel("LOGGING ON", StatusTimeout);
        var (_, streaming) = WaitForPlotPointGrowth(PlotGrowthTimeout);

        // Assert — the axis anchors on the new session's own data (issue #573):
        // (a) no negative time (the counter-wrap symptom puts real data ~70+ s below zero);
        Assert.IsTrue(
            streaming.FirstX >= 0d,
            $"The restarted session rendered NEGATIVE axis time (firstx={streaming.FirstX:F0} ms) — " +
            "the session anchored on a prior-session leftover frame across a counter wrap.");

        // (b) the first rendered sample sits at the axis origin, not offset into the session;
        Assert.IsTrue(
            streaming.FirstX <= MAX_ANCHOR_OFFSET_MS,
            $"The restarted session's first rendered sample sits at +{streaming.FirstX:F0} ms — " +
            "the time axis did not anchor on the new session's first sample.");

        // (c) the axis spans only the time actually streamed — a session anchored on a leftover
        //     frame spans the stop-to-start gap plus the streamed time instead.
        Assert.IsTrue(
            streaming.LastX < StopStartGap.TotalMilliseconds,
            $"The restarted session's axis reaches +{streaming.LastX:F0} ms after only a few seconds " +
            $"of streaming — the {StopStartGap.TotalSeconds:F0} s stop-to-start gap leaked into the " +
            "time axis (leftover prior-session frame anchored the session).");

        // Stop session 2 and let both sessions finalize.
        StopLogging();
        WaitForLoggingStatusLabel("LOGGING OFF", StatusTimeout);

        // Cleanup — both sessions accrued data, so two new rows exist; delete them to return
        // the persistent test-mode DB to its pre-run baseline.
        WaitForLoggedSessionCount(sessionsBefore + 2, TimeSpan.FromSeconds(20));
        DeleteNewestLoggedSession();
        WaitForExactLoggedSessionCount(sessionsBefore + 1, TimeSpan.FromSeconds(20));
        DeleteNewestLoggedSession();
        WaitForExactLoggedSessionCount(sessionsBefore, TimeSpan.FromSeconds(20));
    }
}
