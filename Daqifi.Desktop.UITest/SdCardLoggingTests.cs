using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daqifi.Desktop.UITest;

/// <summary>
/// Scenario 4 — SD card lifecycle: log to the device, then import what was logged.
/// Drives the real GUI out-of-process in one end-to-end pass: connects to the physically
/// attached USB device, switches the logging mode from "Stream to App" to "Log to Device"
/// (SD card), enables the analog channels, runs a brief logging session, and stops it —
/// asserting SD-card-side evidence (the device's "Enabled/Disabled SD card logging" log
/// lines plus a new file on the SD card) that it logged to its SD card rather than to an
/// in-app stream session. It then imports that just-written file back into the app and
/// asserts a new, non-empty <see cref="Daqifi.Desktop.Logger.LoggingSession"/> appears in
/// the logged-session list — a true round trip that also proves the device's freshly
/// written file actually parses.
///
/// While the session runs it also asserts the Live Graph reflects SD-card mode (issue #507):
/// the necessarily-empty live plot is replaced by the "Logging to Device" status overlay —
/// present in the UIA tree only while the device reports SD-card logging — showing a live
/// HH:mm:ss elapsed clock, and the overlay disappears once logging stops and the plot returns.
///
/// Combining both halves in one test avoids re-running the shared connect/configure setup
/// and makes the imported file's provenance certain (it is the file this run produced).
/// Requires a DAQiFi device with an SD card connected via USB (SD logging is USB-only).
/// </summary>
[TestClass]
public class SdCardLoggingTests : DaqifiAppFixture
{
    #region Constants
    // A gentle frequency keeps the UI responsive for out-of-process automation while the
    // device streams to its SD card.
    private const double TARGET_FREQUENCY_HZ = 100d;

    // How long to let the device log to its SD card before stopping. SD file operations
    // are blocked while logging, so a new file cannot be observed mid-run — this is a
    // small, deliberate run window (not a readiness wait) long enough to create a file
    // with enough samples to import.
    private static readonly TimeSpan SdRunDuration = TimeSpan.FromSeconds(5);
    #endregion

    /// <summary>
    /// End-to-end SD card lifecycle: connect over USB, switch to "Log to Device" mode,
    /// enable channels, run a brief logging session, stop, assert the device logged to its
    /// SD card (Enabled/Disabled log lines, the Live Graph "Logging to Device" overlay shown
    /// while logging and hidden after stop, plus an increased SD file count), then import the
    /// file that was just written and assert a new non-empty logging session appears.
    /// </summary>
    [TestMethod]
    [TestCategory("Ui")]
    [TestCategory("RequiresDevice")]
    public void SdCardLogging_LogsToSdCard_ThenImportsToSession()
    {
        // Arrange — connect over Serial/USB. SD card logging requires USB: the
        // "Log to Device" selector is disabled for WiFi devices, so this scenario
        // forces Serial regardless of DAQIFI_TEST_TRANSPORT.
        ConnectFirstDevice(DeviceTransport.Serial);

        // Baseline the logged-session count (APP LOGS sub-tab) before importing, so the new
        // session the import creates can be proven later. SD-card logging itself does NOT
        // add an in-app session, so this baseline stays valid across the logging run.
        var sessionsBefore = GetLoggedSessionCount();

        // Baseline the SD card file list while the device is still in the default Stream
        // mode. This also asserts the device actually has an SD card present (the helper
        // marks the test inconclusive otherwise). Capture the file names too, so the file
        // the logging run writes can be identified by diffing against this set.
        var filesBefore = GetSdCardFileCount();
        var namesBefore = ReadSdCardFileNames();

        // Act (log) — switch the device to "Log to Device" (SD card) logging mode.
        SetLoggingMode(logToDevice: true);

        // Configure a known rate and enable the analog channels (which gate the toggle).
        SetSamplingFrequency(TARGET_FREQUENCY_HZ);
        var activeChannels = EnableAllAnalogChannels();
        Assert.IsTrue(
            activeChannels > 0,
            "Pre-condition failed: no analog channels became active.");

        // Start logging. In SD mode the same toolbar toggle calls StartSdCardLogging on
        // the device; production logs "Enabled SD card logging for device {serial}" — a
        // mode-specific signal an in-app stream session would never emit.
        StartLogging();
        WaitForLoggingStatusLabel("LOGGING ON", TimeSpan.FromSeconds(15));
        Assert.IsTrue(
            WaitForLogContains("Enabled SD card logging for device", TimeSpan.FromSeconds(20)),
            "Expected the device to engage SD card logging, but no 'Enabled SD card logging' " +
            "log line appeared. Without it the session likely streamed to the app instead of " +
            "logging to the SD card.");

        // Assert (UI) — the Live Graph reflects what's actually happening (issue #507). With the
        // device recording to its own SD card (not streaming to the app), the necessarily-empty
        // live plot is replaced by the centered "Logging to Device" status overlay. It is shown
        // only while IsSdCardLoggingActive, which is driven by the device's IsLoggingToSdCard +
        // PropertyChanged — so its appearance proves the device's logging state actually reached
        // the UI. That is a stronger end-to-end signal than the "Enabled SD card logging" log line
        // above, which the device emits even if its reported state never flipped true.
        WaitForSdLoggingOverlay(shouldBeDisplayed: true, timeout: TimeSpan.FromSeconds(15));

        // The overlay's elapsed clock is live — a 1 Hz timer drives the bound HH:mm:ss value.
        // Reading it back as a well-formed clock confirms the panel shows real, ticking content,
        // not merely that an empty Border is present in the tree.
        var elapsed = ReadSdLoggingElapsed();
        StringAssert.Matches(
            elapsed,
            new System.Text.RegularExpressions.Regex(@"^\d{2}:\d{2}:\d{2}$"),
            $"Expected the 'Logging to Device' overlay to show an HH:mm:ss elapsed clock while SD " +
            $"logging, but read '{elapsed}'. The SdLoggingElapsed timer may not be running.");

        // Let the device log to its SD card briefly.
        System.Threading.Thread.Sleep(SdRunDuration);

        // Stop logging — production logs "Disabled SD card logging for device {serial}".
        StopLogging();
        WaitForLoggingStatusLabel("LOGGING OFF", TimeSpan.FromSeconds(15));
        Assert.IsTrue(
            WaitForLogContains("Disabled SD card logging for device", TimeSpan.FromSeconds(20)),
            "Expected the device to stop SD card logging, but no 'Disabled SD card logging' " +
            "log line appeared.");

        // Assert (UI) — stopping SD logging hides the overlay and brings the live plot back.
        // IsSdCardLoggingActive returns to false (device IsLoggingToSdCard=false + PropertyChanged),
        // collapsing the "Logging to Device" Border out of the UIA tree.
        WaitForSdLoggingOverlay(shouldBeDisplayed: false, timeout: TimeSpan.FromSeconds(15));

        // Assert (out-of-process) — a new file exists on the SD card. The device writes a
        // log file to its SD card per session, so an increased file count is positive proof
        // the run logged to the SD card, not to an in-app stream session. The helper polls
        // (re-refreshing) under a single overall timeout to ride out brief device-side
        // finalize lag after stop.
        var filesAfter = WaitForSdCardFileCountAbove(filesBefore, TimeSpan.FromSeconds(30));
        Assert.IsTrue(
            filesAfter > filesBefore,
            $"Expected the SD card file count to increase after an SD logging run " +
            $"(before={filesBefore}, after={filesAfter}). No new SD file means the device did " +
            "not log to its SD card.");

        // Identify the file this run just wrote: the name present now but not before.
        var namesAfter = ReadSdCardFileNames();
        var newFile = namesAfter.FirstOrDefault(
            n => !namesBefore.Contains(n, StringComparer.OrdinalIgnoreCase));

        // Prefer a staged "error"-prefixed file when one is present on the card: importing it
        // proves such files are both listed and importable, guarding daqifi-core #195 (where
        // "error*" filenames were wrongly dropped from the SD listing). The harness cannot make
        // the device write an error-named file, so this exercises #195 only when a fixture file
        // is staged on the card; otherwise it imports the file this run just wrote (a true
        // write -> read round trip). Either way the import must yield a non-empty session.
        var errorFile = namesAfter.FirstOrDefault(
            n => n.StartsWith("error", StringComparison.OrdinalIgnoreCase));
        if (errorFile != null)
        {
            TestContext?.WriteLine(
                $"Found staged 'error'-prefixed SD file '{errorFile}'; importing it to guard daqifi-core #195.");
        }

        // Act (import) — import the chosen file back into the app. Passing its exact name makes
        // the normal case a true round trip; if no file could be identified the helper falls
        // back to the first file so the import path is still exercised.
        var importedFile = ImportSdCardFile(targetFileName: errorFile ?? newFile);

        // Assert (import, log) — the imported session holds real sample data. The importer
        // logs "Imported N samples for session ..."; N must be greater than zero, proving the
        // freshly written SD file parsed into a non-empty session.
        var importedSampleCount = WaitForImportedSampleCount(TimeSpan.FromSeconds(30));
        Assert.IsTrue(
            importedSampleCount > 0,
            $"Expected importing the just-logged SD file '{importedFile}' to yield a non-empty " +
            $"session, but the importer reported {importedSampleCount} samples. An empty import " +
            "means the device's freshly written file did not parse into samples.");

        // Assert (import, log) — the device import path completed successfully, and (when the
        // file name was read) for the very file we selected. Tying the assertion to the chosen
        // name confirms the new session corresponds to the imported file and validates the row
        // read; falls back to the generic fragment if the name could not be read.
        var successFragment = string.IsNullOrEmpty(importedFile)
            ? "Successfully imported"
            : $"Successfully imported '{importedFile}'";
        Assert.IsTrue(
            WaitForLogContains(successFragment, TimeSpan.FromSeconds(10)),
            $"Expected a \"{successFragment} ... from device\" log line after the import, but " +
            "none appeared. The download/parse failed, or a different file was imported than the " +
            "one selected.");

        // Assert (import, UI) — a new row appears in the logged-session list. The import adds
        // the session to LoggingManager.LoggingSessions on the UI thread, so LoggedSessionList
        // (APP LOGS sub-tab) gains a row. SD-card logging alone never adds one, so this delta
        // is attributable to the import.
        WaitForLoggedSessionCount(sessionsBefore + 1, TimeSpan.FromSeconds(30));
        var sessionsAfter = GetLoggedSessionCount();
        Assert.IsTrue(
            sessionsAfter > sessionsBefore,
            $"Expected a new logged session after importing '{importedFile}' " +
            $"(before={sessionsBefore}, after={sessionsAfter}). No new row in LoggedSessionList " +
            "means the imported session was not surfaced to the app.");

        CaptureScreenshot("SdCardLogging_LogsToSdCard_ThenImports_final");

        // Best-effort: leave the device back in Stream mode for the next run. The base
        // fixture's [TestCleanup] closes the app regardless, so a failure here is benign.
        try { SetLoggingMode(logToDevice: false); } catch { /* teardown closes the app */ }

        // Per-test independence: the base fixture's [TestCleanup] closes the app, which
        // disconnects the device. A fresh app instance is launched per test.
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
