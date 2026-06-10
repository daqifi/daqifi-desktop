using System;
using System.IO.Ports;
using System.Linq;
using FlaUI.Core.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daqifi.Desktop.UITest;

/// <summary>
/// Manual-USB connect scenarios for issue #524 — graceful handling of a missing / mistyped COM
/// port. Drives the real GUI out-of-process: opens the connection dialog's Manual USB tab, types a
/// COM port by hand, connects, and asserts the user-visible outcome (inline error vs. successful
/// connect) plus the NLog log.
///
/// Before the fix, connecting to an absent COM port let <c>SerialPort.Open</c> throw
/// <c>FileNotFoundException</c>, which was logged at error level (the sole Sentry-capture path) and
/// closed the dialog silently. The fix pre-validates the typed port against the system's enumerated
/// ports, surfaces a friendly inline message, keeps the dialog open, and logs only a warning.
///
/// Requires a DAQiFi device physically attached (USB).
/// </summary>
[TestClass]
public class ManualSerialConnectTests : DaqifiAppFixture
{
    // A syntactically valid COM name that is not present on any realistic bench, so pre-validation
    // rejects it before any SerialPort.Open is attempted.
    private const string NONEXISTENT_PORT = "COM250";

    [TestMethod]
    [TestCategory("Ui")]
    [TestCategory("RequiresDevice")]
    public void ManualSerial_NonexistentPort_ShowsInlineError_KeepsDialogOpen_NoErrorLog()
    {
        Assert.AreEqual(0, GetConnectedDeviceCount(), "Expected no device connected at test start.");

        var dialog = OpenManualSerialTab();
        SetManualPortName(dialog, NONEXISTENT_PORT);
        InvokeManualSerialConnect(dialog);

        // The inline validation message appears and names the offending port.
        var error = Retry.WhileNull(
            () => ReadManualPortError(dialog),
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(200),
            throwOnTimeout: false,
            ignoreException: true).Result;

        // Evidence: capture the dialog showing the red inline error (PASS-path screenshot — the base
        // fixture only screenshots on failure). Best-effort; never let capture mask the assertion.
        try
        {
            var shot = CaptureElementPng(dialog, "manual_port_nonexistent_error.png");
            TestContext.WriteLine($"Screenshot: {shot}");
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"Screenshot capture skipped: {ex.Message}");
        }

        Assert.IsNotNull(error, "Expected an inline ManualPortError after connecting to a nonexistent port.");
        StringAssert.Contains(error, NONEXISTENT_PORT, "Inline error should name the offending port.");

        // The dialog must stay open (no silent close) and no device should connect.
        Assert.IsTrue(dialog.IsAvailable, "The connection dialog should stay open after a failed manual connect.");
        Assert.AreEqual(0, GetConnectedDeviceCount(), "No device should connect for a nonexistent port.");

        // The fix: this path logs a WARNING (no Sentry) and never reaches SerialStreamingDevice.Connect's
        // generic catch — assert the warning is present and the error/Sentry line is absent.
        Assert.IsTrue(
            WaitForLogContains("Manual serial connect rejected", TimeSpan.FromSeconds(5)),
            "Expected a warning-level 'Manual serial connect rejected' log line for the missing port.");
        var log = ReadNewLogText();
        Assert.IsFalse(
            log.Contains("Failed to connect on", StringComparison.Ordinal),
            "The missing-port path must NOT log SerialStreamingDevice's error (the Sentry capture path).");
    }

    [TestMethod]
    [TestCategory("Ui")]
    [TestCategory("RequiresDevice")]
    public void ManualSerial_InlineError_ClearsWhenPortNameEdited()
    {
        var dialog = OpenManualSerialTab();
        SetManualPortName(dialog, NONEXISTENT_PORT);
        InvokeManualSerialConnect(dialog);

        var error = Retry.WhileNull(
            () => ReadManualPortError(dialog),
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(200),
            throwOnTimeout: false,
            ignoreException: true).Result;
        Assert.IsNotNull(error, "Expected the inline error to appear before editing the port name.");

        // Editing the COM port should clear the stale validation error (OnManualPortNameChanged).
        SetManualPortName(dialog, "COM1");
        var cleared = Retry.WhileFalse(
            () => ReadManualPortError(dialog) == null,
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(200),
            throwOnTimeout: false,
            ignoreException: true).Result;

        Assert.IsTrue(cleared, "Editing the COM port name should clear the inline validation error.");
    }

    [TestMethod]
    [TestCategory("Ui")]
    [TestCategory("RequiresDevice")]
    public void ManualSerial_RealDevicePort_Connects()
    {
        var port = ResolveSingleSerialPort();
        Assert.AreEqual(0, GetConnectedDeviceCount(), "Expected no device connected at test start.");

        var dialog = OpenManualSerialTab();
        SetManualPortName(dialog, port);
        InvokeManualSerialConnect(dialog);

        // Pre-validation must accept a real, enumerated port; the manual connect should then succeed
        // end-to-end, signalled by a device tile appearing in the connected-devices container.
        WaitForConnectedDeviceCount(1, TimeSpan.FromSeconds(60));
        Assert.IsTrue(
            GetConnectedDeviceCount() >= 1,
            $"Manual connect to the real device port {port} should connect the device.");

        // No inline error should be shown on the success path (guard: gotcha #4 — the modal may or
        // may not have auto-closed under automation).
        if (dialog.IsAvailable)
        {
            Assert.IsNull(
                ReadManualPortError(dialog),
                "No inline error should appear when manually connecting to a valid port.");
            try { dialog.Close(); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Resolves the COM port of the attached DAQiFi. Honors <c>DAQIFI_TEST_SERIAL_PORT</c> when set;
    /// otherwise uses the sole enumerated serial port. Marks the test inconclusive (not failed) when
    /// the port cannot be identified unambiguously, so it never produces a false negative on a bench
    /// with zero or multiple COM ports.
    /// </summary>
    private static string ResolveSingleSerialPort()
    {
        var configured = Environment.GetEnvironmentVariable("DAQIFI_TEST_SERIAL_PORT");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        var ports = SerialPort.GetPortNames().Distinct().ToArray();
        if (ports.Length == 1)
        {
            return ports[0];
        }

        Assert.Inconclusive(
            $"Could not identify the DAQiFi port unambiguously: found {ports.Length} serial port(s) " +
            $"({string.Join(", ", ports)}). Set DAQIFI_TEST_SERIAL_PORT to choose one.");
        return null!; // unreachable — Assert.Inconclusive throws.
    }
}
