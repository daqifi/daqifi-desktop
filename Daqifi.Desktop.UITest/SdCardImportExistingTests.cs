using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daqifi.Desktop.UITest;

/// <summary>
/// Focused end-to-end confirmation of the SD-card DOWNLOAD + IMPORT path alone, against a
/// file already present on the card — deliberately decoupled from SD-card WRITE.
///
/// The full lifecycle test (<see cref="SdCardLoggingTests"/>) logs a fresh file and then
/// imports it; when the device's SD WRITE side is wedged (no new file is finalized, the
/// known intermittent bench state) that test fails at the write step and never exercises
/// the download/import it shares with this one. This test connects over USB, lists the SD
/// files already on the card, and imports the first one through the real DEVICE LOGS IMPORT
/// button — driving the same production path the desktop uses
/// (<c>SdCardSessionImporter.ImportFromDeviceAsync</c> -&gt; Core
/// <c>DownloadSdCardFileAsync</c> -&gt; temp file -&gt; protobuf parse -&gt; SQLite bulk
/// insert) — and asserts a non-empty session results. It is the GUI-level counterpart to the
/// raw-serial proof that the device serves the file's bytes correctly.
///
/// Requires a DAQiFi device with an SD card that already holds at least one log file,
/// connected via USB (SD operations are USB-only).
/// </summary>
[TestClass]
public class SdCardImportExistingTests : DaqifiAppFixture
{
    /// <summary>
    /// Connects over USB and imports a log file already present on the device's SD card
    /// through the real DEVICE LOGS IMPORT button, asserting the importer reports a
    /// non-empty session (samples &gt; 0), logs a success line, and adds a logged-session
    /// row. This exercises the download + parse + insert path on its own, independently of
    /// SD-card WRITE health. A deterministic target is chosen (newest <c>.bin</c> device log)
    /// so the same file imports across runs. Marks the test inconclusive when the card holds
    /// no files to import.
    /// </summary>
    [TestMethod]
    [TestCategory("Ui")]
    [TestCategory("RequiresDevice")]
    public void SdCardImport_ExistingFileOnCard_ImportsToSession()
    {
        // Arrange — connect over USB (SD is USB-only).
        ConnectFirstDevice(DeviceTransport.Serial);

        var sessionsBefore = GetLoggedSessionCount();

        // The card must already hold a file to import. GetSdCardFileCount marks the test
        // inconclusive if no SD card is present. An empty-but-present card is an environment
        // precondition, not a product failure, so treat it as inconclusive too (mirroring the
        // fixture's "No SD card" handling) rather than a false-negative bench failure.
        var fileCount = GetSdCardFileCount();
        if (fileCount == 0)
        {
            Assert.Inconclusive(
                "No files on the SD card to import. Stage at least one log file and re-run.");
        }

        // Pick a deterministic target: the device's file list applies no stable ordering and
        // can mix .bin/.json/.csv, so "first row" varies across firmware responses. Prefer the
        // newest .bin (the device-native log format), falling back to the newest importable
        // file of any kind, then to first-file selection if names can't be read.
        var fileNames = ReadSdCardFileNames();
        var targetFileName =
            fileNames.Where(n => n.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                     .LastOrDefault()
            ?? fileNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .LastOrDefault();

        // Act — import the chosen file through the real DEVICE LOGS IMPORT button. This
        // downloads it over USB (Core DownloadSdCardFileAsync) and imports it. A null/empty
        // target falls back to the first file inside ImportSdCardFile.
        var importedFile = ImportSdCardFile(targetFileName);

        // Assert (log) — the importer logged "Imported N samples ..." with N > 0, proving the
        // downloaded file parsed into a non-empty session. This is the assertion that the
        // original 0-byte defect would fail.
        var importedSampleCount = WaitForImportedSampleCount(TimeSpan.FromSeconds(60));
        Assert.IsTrue(
            importedSampleCount > 0,
            $"Expected importing existing SD file '{importedFile}' to yield a non-empty session, " +
            $"but the importer reported {importedSampleCount} samples. A 0 here is the original " +
            "download-returns-empty defect reproducing.");

        // Assert (log) — the device import path completed successfully for the chosen file.
        var successFragment = string.IsNullOrEmpty(importedFile)
            ? "Successfully imported"
            : $"Successfully imported '{importedFile}'";
        Assert.IsTrue(
            WaitForLogContains(successFragment, TimeSpan.FromSeconds(10)),
            $"Expected a \"{successFragment} ... from device\" log line after the import, but none " +
            "appeared. The download/parse failed.");

        // Assert (UI) — a new row appears in the logged-session list.
        WaitForLoggedSessionCount(sessionsBefore + 1, TimeSpan.FromSeconds(30));
        var sessionsAfter = GetLoggedSessionCount();
        Assert.IsTrue(
            sessionsAfter > sessionsBefore,
            $"Expected a new logged session after importing '{importedFile}' " +
            $"(before={sessionsBefore}, after={sessionsAfter}).");
    }
}
