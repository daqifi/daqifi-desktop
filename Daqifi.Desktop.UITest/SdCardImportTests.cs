using System;
using System.Globalization;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daqifi.Desktop.UITest;

/// <summary>
/// Scenario 5 — SD card session import. Drives the real GUI out-of-process: connects to
/// the physically attached USB device, opens the Logged Data pane's DEVICE LOGS sub-tab,
/// refreshes the SD card file list, imports one file, and asserts a new, non-empty
/// <c>LoggingSession</c> appears in the logged-session list. Success is triangulated from
/// three independent black-box signals — the app's "Import Complete" dialog, the importer's
/// "Imported N samples" log line (N &gt; 0), and a +1 delta in <c>LoggedSessionList</c> —
/// so a silent empty/failed import cannot pass.
///
/// SD card access is USB-only, so this scenario forces Serial regardless of
/// <c>DAQIFI_TEST_TRANSPORT</c>. It is inconclusive (not failed) when the device has no SD
/// card or no log files to import, since those are bench-setup conditions, not regressions.
/// Requires a DAQiFi device with at least one SD card log file connected via USB.
///
/// When a fixture file whose name starts with "error" is staged on the card, the import
/// targets it (guarding daqifi-core #195, where "error"-prefixed filenames were wrongly
/// dropped from the listing); otherwise it imports the first file.
/// </summary>
[TestClass]
public class SdCardImportTests : DaqifiAppFixture
{
    [TestMethod]
    [TestCategory("Ui")]
    [TestCategory("RequiresDevice")]
    public void ImportSdCardFile_CreatesNonEmptyLoggingSession()
    {
        // Arrange — connect over Serial/USB. SD card access requires USB, so this scenario
        // forces Serial regardless of DAQIFI_TEST_TRANSPORT.
        ConnectFirstDevice(DeviceTransport.Serial);

        // Record the logged-session count before importing (on the APP LOGS sub-tab) so we
        // can prove a brand-new session was created by the import.
        var sessionsBefore = GetLoggedSessionCount();

        // Refresh the SD card file list and confirm there is something to import. The helper
        // marks the test inconclusive when no SD card is installed (and fails on an SD card
        // error); a zero count is a valid "SD card OK · 0 files" state that this scenario
        // cannot exercise, so treat it as inconclusive too.
        var filesAvailable = GetSdCardFileCount();
        if (filesAvailable == 0)
        {
            Assert.Inconclusive(
                "The attached device's SD card has no log files to import. Log a session to " +
                "the SD card (see SdCardLoggingTests) or stage a file, then re-run.");
        }

        // Act — select a file and import it. Prefers an "error"-prefixed file when present.
        var importedFile = ImportSdCardFile(preferNamePrefix: "error");

        // Assert (out-of-process, log) — the imported session holds real sample data. The
        // importer logs "Imported N samples for session ..."; N must be greater than zero,
        // which proves the session's sample/channel data is non-empty.
        var importedSampleCount = WaitForImportedSampleCount(TimeSpan.FromSeconds(30));
        Assert.IsTrue(
            importedSampleCount > 0,
            $"Expected the imported SD card file '{importedFile}' to yield a non-empty session, " +
            $"but the importer reported {importedSampleCount} samples. An empty import means the " +
            "file had no parseable data (or the parser dropped every line).");

        // Assert (out-of-process, log) — the device import path completed successfully.
        Assert.IsTrue(
            WaitForLogContains("Successfully imported", TimeSpan.FromSeconds(10)),
            "Expected a 'Successfully imported ... from device' log line after the import, but " +
            "none appeared. The download or parse likely failed.");

        // Assert (out-of-process, UI) — a new row appears in the logged-session list. The
        // import adds the session to LoggingManager.LoggingSessions on the UI thread, so the
        // LoggedSessionList (APP LOGS sub-tab) gains exactly one row. This is the issue's
        // primary acceptance signal.
        WaitForLoggedSessionCount(sessionsBefore + 1, TimeSpan.FromSeconds(30));
        var sessionsAfter = GetLoggedSessionCount();
        Assert.IsTrue(
            sessionsAfter > sessionsBefore,
            $"Expected a new logged session after importing '{importedFile}' " +
            $"(before={sessionsBefore}, after={sessionsAfter}). No new row in LoggedSessionList " +
            "means the imported session was not surfaced to the app.");

        CaptureScreenshot("ImportSdCardFile_CreatesNonEmptyLoggingSession_final");

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
