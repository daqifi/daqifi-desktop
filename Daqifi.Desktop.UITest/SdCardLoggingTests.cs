using System;
using System.Globalization;
using System.IO;
using FlaUI.Core.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daqifi.Desktop.UITest;

/// <summary>
/// Scenario 4 — SD card logging mode. Drives the real GUI out-of-process: connects to
/// the physically attached USB device, switches the logging mode from "Stream to App"
/// to "Log to Device" (SD card), enables the analog channels, runs a brief logging
/// session, and stops it. Asserts SD-card-side evidence — the device's "Enabled/Disabled
/// SD card logging" log lines plus a new file on the SD card (the file count increased) —
/// confirming the device logged to its SD card rather than to an in-app stream session.
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
    // small, deliberate run window (not a readiness wait) long enough to create a file.
    private static readonly TimeSpan SdRunDuration = TimeSpan.FromSeconds(5);
    #endregion

    [TestMethod]
    [TestCategory("Ui")]
    [TestCategory("RequiresDevice")]
    public void SdCardLogging_LogsToSdCard_NotStream()
    {
        // Arrange — connect over Serial/USB. SD card logging requires USB: the
        // "Log to Device" selector is disabled for WiFi devices, so this scenario
        // forces Serial regardless of DAQIFI_TEST_TRANSPORT.
        ConnectFirstDevice(DeviceTransport.Serial);

        // Baseline the SD card file count while the device is still in the default
        // Stream mode. This also asserts the device actually has an SD card present
        // (the helper marks the test inconclusive otherwise).
        var filesBefore = GetSdCardFileCount();

        // Act — switch the device to "Log to Device" (SD card) logging mode.
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

        // Let the device log to its SD card briefly.
        System.Threading.Thread.Sleep(SdRunDuration);

        // Stop logging — production logs "Disabled SD card logging for device {serial}".
        StopLogging();
        WaitForLoggingStatusLabel("LOGGING OFF", TimeSpan.FromSeconds(15));
        Assert.IsTrue(
            WaitForLogContains("Disabled SD card logging for device", TimeSpan.FromSeconds(20)),
            "Expected the device to stop SD card logging, but no 'Disabled SD card logging' " +
            "log line appeared.");

        // Assert (out-of-process) — a new file exists on the SD card. The device writes a
        // log file to its SD card per session, so an increased file count is positive
        // proof the run logged to the SD card, not to an in-app stream session. Poll
        // (re-refreshing) to ride out any brief device-side finalize lag after stop.
        var filesAfter = filesBefore;
        var sdFileAppeared = Retry.WhileFalse(
            () =>
            {
                filesAfter = GetSdCardFileCount();
                return filesAfter > filesBefore;
            },
            timeout: TimeSpan.FromSeconds(30),
            interval: TimeSpan.FromSeconds(2),
            throwOnTimeout: false).Result;

        Assert.IsTrue(
            sdFileAppeared,
            $"Expected the SD card file count to increase after an SD logging run " +
            $"(before={filesBefore}, after={filesAfter}). No new SD file means the device did " +
            "not log to its SD card.");

        CaptureScreenshot("SdCardLogging_LogsToSdCard_final");

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
