using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daqifi.Desktop.UITest;

/// <summary>
/// Scenario — device friendly name (issue #83). Drives the real GUI out-of-process to prove:
/// (1) the device's serial number is shown in the settings drawer, and (2) a user-set friendly
/// name round-trips through <c>SYSTem:DEVice:NAME</c> / <c>SYSTem:DEVice:NAME:SAVE</c> and
/// actually persists on the device's NVM — not just the app's in-session state — by
/// disconnecting, reconnecting the same physical device, and reading the name back from a fresh
/// streaming frame. Requires a DAQiFi device.
/// </summary>
[TestClass]
public class FriendlyNameTests : DaqifiAppFixture
{
    #region Constants
    // A gentle frequency keeps the UI responsive for out-of-process automation while briefly
    // streaming (mirrors ConnectionLifecycleTests/LoggingSessionTests).
    private const double TARGET_FREQUENCY_HZ = 100d;

    private static readonly TimeSpan PlotGrowthTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StatusLabelTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan TeardownTimeout = TimeSpan.FromSeconds(30);
    #endregion

    [TestMethod]
    [TestCategory("Ui")]
    [TestCategory("RequiresDevice")]
    public void SetFriendlyName_PersistsAcrossReconnect()
    {
        var transport = ResolveTransport();

        // Arrange — connect and confirm the drawer shows the device's serial number (issue #83's
        // "show device SN under device settings" half).
        ConnectFirstDevice(transport);
        var serial = GetSerialNumberInDrawer();
        Assert.IsFalse(
            string.IsNullOrWhiteSpace(serial),
            "The device settings drawer's SERIAL value was blank — the device SN is not shown.");

        // A name unique enough to distinguish "we read a stale/leftover value" from "we read the
        // value we just set", but still well inside firmware's 31-char / printable-ASCII limit.
        var targetName = "UITest-" + DateTime.Now.ToString("HHmmss", System.Globalization.CultureInfo.InvariantCulture);

        // Act — set the friendly name through the drawer and confirm the app's own "saved" signal.
        SetFriendlyNameInDrawer(targetName);
        Assert.AreEqual(
            targetName, GetFriendlyNameInDrawer(),
            "The NAME field did not show the value just saved (optimistic local update).");
        CaptureScreenshot("FriendlyName_AfterSave");

        // Act — disconnect and reconnect the SAME physical device. A fresh connection has no
        // in-session FriendlyName state, so any value the drawer shows after this must have come
        // from the device itself.
        DisconnectSelectedDevice();
        WaitForNoConnectedDevices(TeardownTimeout);
        ConnectFirstDevice(transport);

        // FriendlyName is only populated from a genuine streaming frame (analog/digital sample
        // data), not the connect-time info handshake — see AbstractStreamingDevice.OnStreamMessageReceived.
        // Briefly stream to receive one.
        SetSamplingFrequency(TARGET_FREQUENCY_HZ);
        var activeChannels = EnableAllAnalogChannels();
        Assert.IsTrue(activeChannels > 0, "No analog channels became active on the reconnected device.");

        StartLogging();
        WaitForLoggingStatusLabel("LOGGING ON", StatusLabelTimeout);
        WaitForPlotPointGrowth(PlotGrowthTimeout);
        StopLogging();
        WaitForLoggingStatusLabel("LOGGING OFF", StatusLabelTimeout);

        // Assert — the reconnected device reports the name we set before disconnecting, proving
        // it was written to and read back from firmware NVM, not just remembered by the app.
        var nameAfterReconnect = GetFriendlyNameInDrawer();
        var firmwareVersion = GetFirmwareVersionInDrawer();
        Assert.AreEqual(
            targetName, nameAfterReconnect,
            "The friendly name did not survive a disconnect/reconnect — it did not persist to " +
            "the device's NVM (SYSTem:DEVice:NAME:SAVE), or was not read back correctly. " +
            $"Device-reported firmware version: '{firmwareVersion}' (requires >= 3.7.1 for #14).");
        CaptureScreenshot("FriendlyName_AfterReconnect");

        // Per-test independence: the base fixture's [TestCleanup] disconnects and closes the app.
    }

    /// <summary>
    /// Saves a screenshot of the main window into the test results directory and registers it as
    /// a result file. Best-effort; never throws into the test (mirrors ConnectionLifecycleTests).
    /// </summary>
    private void CaptureScreenshot(string name)
    {
        try
        {
            var outDir = TestContext?.TestResultsDirectory ?? AppContext.BaseDirectory;
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
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
