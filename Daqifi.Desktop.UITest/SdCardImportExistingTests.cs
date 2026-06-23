using System;
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
    [TestMethod]
    [TestCategory("Ui")]
    [TestCategory("RequiresDevice")]
    public void SdCardImport_ExistingFileOnCard_ImportsToSession()
    {
        // Arrange — connect over USB (SD is USB-only).
        ConnectFirstDevice(DeviceTransport.Serial);

        var sessionsBefore = GetLoggedSessionCount();

        // The card must already hold a file to import. GetSdCardFileCount marks the test
        // inconclusive if no SD card is present; assert a file exists to import.
        var fileCount = GetSdCardFileCount();
        Assert.IsTrue(
            fileCount > 0,
            "Pre-condition failed: no files on the SD card to import. Stage at least one log file.");

        // Act — import the first file on the card through the real DEVICE LOGS IMPORT button.
        // This downloads it over USB (Core DownloadSdCardFileAsync) and imports it.
        var importedFile = ImportSdCardFile();

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
