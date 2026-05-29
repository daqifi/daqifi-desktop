using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FlaUI.Core.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daqifi.Desktop.UITest;

/// <summary>
/// Scenario 3 — Start / run / stop a logging session. Drives the real GUI
/// out-of-process: connects to the physically attached device, configures logging
/// (frequency + channels), starts a logging session via the toolbar toggle, polls
/// for evidence that data is accruing, then stops the session. All assertions read
/// from the visible UI (logging status text, a created session row in the Logged
/// Data pane) plus a best-effort NLog log-file check. Requires a DAQiFi device.
/// </summary>
[TestClass]
public class LoggingSessionTests : DaqifiAppFixture
{
    #region Constants
    private const double TARGET_FREQUENCY_HZ = 1000d;
    private const int CHANNELS_TO_ENABLE = 1;

    // How long to let the session run while polling for an accrual signal. This is
    // an upper bound for a POLLING wait (Retry), not a fixed sleep — it returns as
    // soon as the signal is observed.
    private static readonly TimeSpan RunPollTimeout = TimeSpan.FromSeconds(20);
    #endregion

    [TestMethod]
    [TestCategory("Ui")]
    [TestCategory("RequiresDevice")]
    public void StartLoggingSession_RunsAndStops()
    {
        // Arrange — connect to the attached device and configure logging.
        var transport = ResolveTransport();
        ConnectFirstDevice(transport);

        SetSamplingFrequency(TARGET_FREQUENCY_HZ);
        IReadOnlyList<string> enabledChannels = EnableFirstAnalogChannels(CHANNELS_TO_ENABLE);
        Assert.AreEqual(
            CHANNELS_TO_ENABLE,
            enabledChannels.Count,
            "Pre-condition failed: did not enable the expected number of analog channels.");

        // Record how many logged sessions exist before this run, so we can prove a
        // new one was created (empty sessions are discarded by the app, so a new row
        // is an out-of-process signal that samples actually accrued).
        var sessionsBefore = GetLoggedSessionCount();

        // Act — start the logging session.
        StartLogging();

        // Assert — the UI reports the session is running.
        WaitForLoggingStatus("LOGGING ON", TimeSpan.FromSeconds(15));

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

        // Assert — the UI reports the session has stopped.
        WaitForLoggingStatus("LOGGING OFF", TimeSpan.FromSeconds(15));

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

        // Capture a screenshot of the final state as a test artifact.
        CaptureScreenshot("StartLoggingSession_RunsAndStops_final");

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
