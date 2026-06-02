using System;
using FlaUI.Core.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daqifi.Desktop.UITest;

/// <summary>
/// Scenario 1 — Add device. Drives the real GUI out-of-process to open the
/// connection dialog, discover the physically attached device, and connect to it.
/// Asserts via the visible UI (dialog closes + a device tile appears in the
/// connected-devices container). Requires a DAQiFi device to be attached.
/// </summary>
[TestClass]
public class AddDeviceTests : DaqifiAppFixture
{
    [TestMethod]
    [TestCategory("Ui")]
    [TestCategory("RequiresDevice")]
    public void AddDevice_ConnectsToAttachedDevice()
    {
        // Arrange — transport is configurable via DAQIFI_TEST_TRANSPORT (Wifi|Serial),
        // defaulting to Serial. The base fixture has already launched the app.
        var transport = ResolveTransport();

        // Sanity: start from a clean slate (no device connected yet).
        Assert.AreEqual(
            0,
            GetConnectedDeviceCount(),
            "Expected no connected devices at the start of the test.");

        // Act — run the full add-device workflow via the reusable helper.
        ConnectFirstDevice(transport);

        // Assert — the connection dialog has closed and a device tile is shown.
        // (Both conditions are enforced inside ConnectFirstDevice; re-assert here
        // for an explicit, test-visible check using only the out-of-process UI.)
        var connected = Retry.WhileFalse(
            () => GetConnectedDeviceCount() >= 1,
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: false);

        Assert.IsTrue(
            connected.Result,
            $"No device tile appeared in the connected-devices list for {transport} transport.");

        // Per-test independence: the base fixture's [TestCleanup] closes the app,
        // which disconnects the device. A fresh app instance is launched per test.
    }
}
