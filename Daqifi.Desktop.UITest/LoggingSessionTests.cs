using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FlaUI.Core.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daqifi.Desktop.UITest;

/// <summary>
/// Scenario 3 — Start / run / stop a logging session, asserting the <b>live plot renders
/// believable data while streaming</b> (issue #560), then delete the session (issue #557).
/// Drives the real GUI out-of-process: connects to the physically attached device,
/// configures logging (frequency + channels), starts a logging session via the toolbar
/// toggle, and — while it streams — reads the Live Graph pane's plot-stats hook to assert the
/// plot is genuinely rendering data: one series per active channel, a rendered point count that
/// strictly increases over a window (flowing, not frozen), and sample values that are finite,
/// in a plausible range, and not a dead flatline at zero. It also polls for the DB-level accrual
/// signal (a new logged-session row), stops the session, and asserts the plot then stops
/// accruing. Finally it deletes the just-created session via its per-row DELETE action (accepting
/// the app's in-pane confirm overlay) and asserts the row is gone and the session count returns
/// to its pre-run baseline — which also leaves the persistent test-mode DB self-cleaned rather
/// than leaking one session per run. All assertions read from the visible/accessible UI (the
/// plot-stats hook, logging status text, the Logged Data session list) plus the app's NLog log
/// file — never app internals. Requires a DAQiFi device.
/// </summary>
[TestClass]
public class LoggingSessionTests : DaqifiAppFixture
{
    #region Constants
    // A gentle frequency keeps the UI responsive enough for out-of-process automation
    // while a session streams (the Logged Data pane queries the same DB the logger is
    // writing and renders a live minimap). Scenario 2 covers the 1000 Hz case.
    private const double TARGET_FREQUENCY_HZ = 100d;

    // How long to let the session run while polling for an accrual signal. This is
    // an upper bound for a POLLING wait (Retry), not a fixed sleep — it returns as
    // soon as the signal is observed.
    private static readonly TimeSpan RunPollTimeout = TimeSpan.FromSeconds(20);

    // Generous magnitude sanity bound for analog sample VALUES (volts). DAQiFi analog inputs
    // are a few volts full-scale; this loose ceiling is not a tight spec but catches obviously
    // broken/garbage scaling while staying hardware-agnostic (the issue's "within the channel's
    // expected range"). Non-finite values are caught separately (NonFiniteCount / IsFinite).
    private const double PLAUSIBLE_ANALOG_CEILING_VOLTS = 1000d;

    // Upper bounds for the live-plot believability polls (return as soon as satisfied).
    private static readonly TimeSpan PlotSeriesTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PlotGrowthTimeout = TimeSpan.FromSeconds(20);

    // How long to allow the live plot's point count to settle to a stable value after logging
    // stops (proving it froze). Generous enough to ride out the ~1 Hz stats refresh catching up
    // to the final pre-stop points plus any brief post-stop pipeline drain.
    private static readonly TimeSpan PlotSettleTimeout = TimeSpan.FromSeconds(15);
    #endregion

    [TestMethod]
    [TestCategory("Ui")]
    [TestCategory("RequiresDevice")]
    public void StartLoggingSession_RendersLivePlot_RunsStopsAndDeletesSession()
    {
        // Arrange — connect to the attached device.
        var transport = ResolveTransport();
        ConnectFirstDevice(transport);

        // Record how many logged sessions exist before this run, while the UI is still
        // quiet (before channels stream), so we can prove a new one was created later
        // (empty sessions are discarded by the app, so a new row is an out-of-process
        // signal that samples actually accrued).
        var sessionsBefore = GetLoggedSessionCount();

        // Configure logging: set frequency and enable the analog channels (SELECT ALL).
        SetSamplingFrequency(TARGET_FREQUENCY_HZ);
        var activeChannels = EnableAllAnalogChannels();
        Assert.IsTrue(
            activeChannels > 0,
            "Pre-condition failed: no analog channels became active.");

        // Act — start the logging session.
        StartLogging();

        // Assert — the user-visible status LABEL (not just the toggle) reads "LOGGING
        // ON". Regression guard: the toggle is the binding source so it always flips,
        // but the "LOGGING ON/OFF" label and the LIVE/MODE/RATE chips only refresh when
        // the IsLogging setter raises PropertyChanged. A bug where it didn't left the
        // toggle On while the label still read "LOGGING OFF". This reads only the label
        // (no toggle-state fallback), so it fails if the label goes stale.
        WaitForLoggingStatusLabel("LOGGING ON", TimeSpan.FromSeconds(15));

        // ── Live plot renders believable data while streaming (issue #560) ──
        // OxyPlot draws points to a single canvas with no per-point UIA elements, so this reads
        // the Live Graph pane's plot-stats hook (PlotStatsText) — visible/accessible UI state,
        // not app internals — to prove the plot is genuinely rendering the streaming data.

        // (a) One series per active channel. Series materialize as each channel first reports,
        //     so this rides out that ramp-up.
        WaitForPlotSeriesCount(activeChannels, PlotSeriesTimeout);

        // (b) The rendered point count strictly increases over a window — data is flowing into
        //     the plot, not frozen.
        var (_, streaming) = WaitForPlotPointGrowth(PlotGrowthTimeout);

        // (c) The rendered sample values are believable: finite (no NaN/Inf), within a plausible
        //     range, and not a dead flatline pinned at exactly zero.
        Assert.AreEqual(
            0L, streaming.NonFiniteCount,
            $"The live plot rendered {streaming.NonFiniteCount} non-finite (NaN/Inf) sample value(s); " +
            "streaming values should all be finite.");
        Assert.IsTrue(
            double.IsFinite(streaming.Last) && double.IsFinite(streaming.Min) && double.IsFinite(streaming.Max),
            $"The live plot's summary stats were not finite (last={streaming.Last}, min={streaming.Min}, " +
            $"max={streaming.Max}) despite {streaming.PointCount} points — the plot is not rendering real values.");
        Assert.IsFalse(
            streaming.Min == 0d && streaming.Max == 0d,
            "The live plot is a dead flatline at exactly zero (min == max == 0) across every channel — " +
            "the value path is broken (real input is essentially never identically zero on all channels).");
        Assert.IsTrue(
            Math.Abs(streaming.Min) <= PLAUSIBLE_ANALOG_CEILING_VOLTS
                && Math.Abs(streaming.Max) <= PLAUSIBLE_ANALOG_CEILING_VOLTS,
            $"The live plot's sample values are outside the plausible range (min={streaming.Min}, " +
            $"max={streaming.Max}, ceiling=±{PLAUSIBLE_ANALOG_CEILING_VOLTS}) — likely broken scaling.");

        // Run — poll (not sleep) for an accrual signal. The session-finalize path only
        // keeps the session if samples were recorded, so a new logged-session row
        // appearing is positive proof of accrual. As a best-effort secondary channel,
        // also watch the NLog log file. We do NOT fail solely on the log file because
        // session start/stop is not guaranteed to emit an Information-level NLog line.
        var accrualSignalSeen = Retry.WhileFalse(
            () => GetLoggedSessionCount() > sessionsBefore,
            timeout: RunPollTimeout,
            interval: TimeSpan.FromMilliseconds(500),
            throwOnTimeout: false).Result;

        // Stop — toggle logging off.
        StopLogging();

        // Assert — the user-visible label (not just the toggle) reads "LOGGING OFF".
        WaitForLoggingStatusLabel("LOGGING OFF", TimeSpan.FromSeconds(15));

        // Assert (issue #560) — with the session inactive, no samples reach the plot, so the
        // rendered point count must stop growing. Proven by the count converging to a stable
        // value (a still-streaming plot would never settle); reads the plot-stats hook.
        AssertPlotStoppedAccruing(PlotSettleTimeout);

        // Assert (out-of-process) — a new logged session exists, proving the session
        // ran and accrued data. If the row had not yet rendered during the run window,
        // give it a final settle window post-stop (the finalize/persist runs on a
        // background thread).
        if (!accrualSignalSeen)
        {
            WaitForLoggedSessionCount(sessionsBefore + 1, TimeSpan.FromSeconds(20));
        }

        var sessionsAfter = GetLoggedSessionCount();
        Assert.IsTrue(
            sessionsAfter > sessionsBefore,
            $"Expected a new logged session after the run (before={sessionsBefore}, " +
            $"after={sessionsAfter}). No new session row means no data accrued.");

        // ── Delete the session we just created (issue #557) ──
        // The scenario continues end-to-end: having proved a session was created, delete it via
        // its per-row DELETE action and accept the in-pane "Delete Confirmation" overlay. This
        // also makes the test self-cleaning — it returns the persistent test-mode DB to its
        // pre-run baseline instead of leaking one session per run.
        DeleteNewestLoggedSession();

        // Assert (out-of-process) — the row is gone and the count returns to the pre-run baseline.
        // This proves the session left the bound LoggingSessions collection (the view). It is NOT,
        // by itself, DB-level proof: DbLogger.DeleteLoggingSession swallows its own exceptions (logs
        // and does not rethrow), so the view-model removes the row even if the SQL delete failed.
        // DB-level deletion is asserted separately, from the app's log lines, just below.
        WaitForExactLoggedSessionCount(sessionsBefore, TimeSpan.FromSeconds(20));
        var sessionsAfterDelete = GetLoggedSessionCount();
        Assert.AreEqual(
            sessionsBefore, sessionsAfterDelete,
            $"Expected the logged-session count to return to its baseline ({sessionsBefore}) " +
            $"after deleting the session created during this run, but it was {sessionsAfterDelete}.");

        // DB-level proof (black box, via the app's own NLog lines). DeleteLoggingSession runs the
        // SQL DELETEs inside a transaction: it logs "DeleteLoggingSession completed" in its finally
        // (so its presence confirms the delete actually ran), and logs "Failed in
        // DeleteLoggingSession" only if the transaction threw (so its absence confirms the delete
        // committed). Together — completed present, failed absent — the rows really left the DB.
        Assert.IsTrue(
            WaitForLogContains("DeleteLoggingSession completed", TimeSpan.FromSeconds(10)),
            "The app never logged 'DeleteLoggingSession completed', so the DB-level delete did not run.");
        Assert.IsFalse(
            ReadNewLogText().Contains("Failed in DeleteLoggingSession", StringComparison.Ordinal),
            "The app logged 'Failed in DeleteLoggingSession' — the SQLite delete transaction threw.");

        // Capture a screenshot of the final state as a test artifact.
        CaptureScreenshot("StartLoggingSession_RendersLivePlot_RunsStopsAndDeletesSession_final");

        // Per-test independence: the base fixture's [TestCleanup] closes the app,
        // which disconnects the device. A fresh app instance is launched per test.
    }

    /// <summary>
    /// Saves a screenshot of the main window into the test results directory and
    /// registers it as a result file. Best-effort; never throws into the test.
    /// </summary>
    private void CaptureScreenshot(string name)
    {
        try
        {
            var outDir = TestContext?.TestResultsDirectory ?? AppContext.BaseDirectory;
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var path = Path.Combine(outDir, $"{name}_{stamp}.png");
            FlaUI.Core.Capturing.Capture.Element(MainWindow).ToFile(path);
            TestContext?.AddResultFile(path);
        }
        catch
        {
            // Screenshot capture must not affect the test outcome.
        }
    }
}
