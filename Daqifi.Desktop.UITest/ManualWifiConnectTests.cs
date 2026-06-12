using System;
using FlaUI.Core.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daqifi.Desktop.UITest;

/// <summary>
/// Manual-WiFi connect scenario for issue #517 — graceful handling of an unreachable device.
/// Drives the real GUI out-of-process: opens the connection dialog's Manual WiFi tab, types an
/// unroutable IP address by hand, connects, and asserts the user-visible outcome (inline error,
/// dialog stays open) plus the NLog log.
///
/// Before the fix, the TCP connect timeout surfaced from Core as a <c>TaskCanceledException</c>
/// that was logged at error level (the sole Sentry-capture path) and the dialog closed silently.
/// The fix classifies the timeout as a warning and keeps the dialog open with an inline message.
///
/// No physical device is required: the scenario types an unroutable address by hand, so these
/// tests carry only <c>[TestCategory("Ui")]</c> (excluded from the unit gate, runnable on a
/// hardware-free bench).
/// </summary>
[TestClass]
public class ManualWifiConnectTests : DaqifiAppFixture
{
    // RFC 5737 TEST-NET-1: guaranteed unroutable, so the TCP connect always exhausts Core's
    // connection timeout (~5s) — the exact failure mode Sentry reported in issue #517.
    private const string UNREACHABLE_IP = "192.0.2.1";

    /// <summary>
    /// Connecting to an unreachable address must show the inline error naming the address,
    /// keep the dialog open, and log the timeout at warning level — never the error-level
    /// (Sentry-capturing) connect-failure line.
    /// </summary>
    [TestMethod]
    [TestCategory("Ui")]
    public void ManualWifi_UnreachableAddress_ShowsInlineError_KeepsDialogOpen_NoErrorLog()
    {
        Assert.AreEqual(0, GetConnectedDeviceCount(), "Expected no device connected at test start.");

        var dialog = OpenManualWifiTab();
        SetManualIpAddress(dialog, UNREACHABLE_IP);
        InvokeManualWifiConnect(dialog);

        // The inline error appears once the connect attempt times out (Core's ~5s connection
        // timeout plus UI marshalling), naming the offending address.
        var error = Retry.WhileNull(
            () => ReadManualWifiError(dialog),
            timeout: TimeSpan.FromSeconds(30),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: false,
            ignoreException: true).Result;

        // Evidence: capture the dialog showing the red inline error (PASS-path screenshot — the base
        // fixture only screenshots on failure). Best-effort; never let capture mask the assertion.
        try
        {
            var shot = CaptureElementPng(dialog, "manual_wifi_unreachable_error.png");
            TestContext.WriteLine($"Screenshot: {shot}");
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"Screenshot capture skipped: {ex.Message}");
        }

        Assert.IsNotNull(error, "Expected an inline ManualWifiError after connecting to an unreachable address.");
        StringAssert.Contains(error, UNREACHABLE_IP, "Inline error should name the offending address.");

        // The dialog must stay open (no silent close) and no device should connect.
        Assert.IsTrue(dialog.IsAvailable, "The connection dialog should stay open after a failed manual connect.");
        Assert.AreEqual(0, GetConnectedDeviceCount(), "No device should connect for an unreachable address.");

        // The fix: the connect timeout is classified as a WARNING (no Sentry). The warning names
        // the endpoint; the old error line (the Sentry capture path) must be absent.
        Assert.IsTrue(
            WaitForLogContains($"{UNREACHABLE_IP}:9760", TimeSpan.FromSeconds(5)),
            "Expected a warning-level log line naming the unreachable endpoint.");
        var log = ReadNewLogText();
        Assert.IsFalse(
            log.Contains("Problem with connecting to DAQiFi Device", StringComparison.Ordinal),
            "The unreachable-device path must NOT log DaqifiStreamingDevice's error (the Sentry capture path).");
    }

    /// <summary>
    /// Editing the IP address after a failed connect must clear the stale inline error
    /// (the <c>OnManualIpAddressChanged</c> clear-on-edit behavior).
    /// </summary>
    [TestMethod]
    [TestCategory("Ui")]
    public void ManualWifi_InlineError_ClearsWhenAddressEdited()
    {
        var dialog = OpenManualWifiTab();
        SetManualIpAddress(dialog, UNREACHABLE_IP);
        InvokeManualWifiConnect(dialog);

        var error = Retry.WhileNull(
            () => ReadManualWifiError(dialog),
            timeout: TimeSpan.FromSeconds(30),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: false,
            ignoreException: true).Result;
        Assert.IsNotNull(error, "Expected the inline error to appear before editing the address.");

        // Editing the address should clear the stale validation error (OnManualIpAddressChanged).
        SetManualIpAddress(dialog, "192.0.2.2");
        var cleared = Retry.WhileFalse(
            () => ReadManualWifiError(dialog) == null,
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(200),
            throwOnTimeout: false,
            ignoreException: true).Result;

        Assert.IsTrue(cleared, "Editing the IP address should clear the inline validation error.");
    }
}
