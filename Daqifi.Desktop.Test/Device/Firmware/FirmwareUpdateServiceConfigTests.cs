using Daqifi.Core.Firmware;
using Daqifi.Desktop.Device.Firmware;

namespace Daqifi.Desktop.Test.Device.Firmware;

/// <summary>
/// Verifies the PIC32 bootloader HID timeouts are widened past Core's 10 s defaults so a long
/// late flash op doesn't trip the firmware update (issue #575).
/// </summary>
[TestClass]
public class FirmwareUpdateServiceConfigTests
{
    [TestMethod]
    public void BootloaderHidTimeout_IsThirtySeconds()
    {
        // Arrange / Act
        var timeout = FirmwareUpdateServiceConfig.BootloaderHidTimeout;

        // Assert
        Assert.AreEqual(TimeSpan.FromSeconds(30), timeout);
    }

    [TestMethod]
    public void CreateBootloaderHidTransport_AppliesTimeoutToReadAndWrite()
    {
        // Act
        var transport = FirmwareUpdateServiceConfig.CreateBootloaderHidTransport();

        // Assert - WriteAsync uses the transport's WriteTimeout (the "HID write failed" path);
        // a stray null-timeout read would use ReadTimeout, so both must be widened.
        Assert.AreEqual(FirmwareUpdateServiceConfig.BootloaderHidTimeout, transport.WriteTimeout);
        Assert.AreEqual(FirmwareUpdateServiceConfig.BootloaderHidTimeout, transport.ReadTimeout);
    }

    [TestMethod]
    public void CreateBootloaderHidTransport_RequestsExclusiveOpen()
    {
        // Act
        var transport = FirmwareUpdateServiceConfig.CreateBootloaderHidTransport();

        // Assert - the flash session must hold the bootloader's HID handle exclusively so no other
        // user-mode opener (the discovery loop, a second app instance) can write a stray frame the
        // CRC-disabled bootloader could mis-parse as an ERASE (A2 stray-write guard).
        Assert.IsTrue(transport.ExclusiveAccess);
    }

    [TestMethod]
    public void CreateOptions_SetsBootloaderResponseTimeout()
    {
        // Act - the service passes BootloaderResponseTimeout to every bootloader read.
        var options = FirmwareUpdateServiceConfig.CreateOptions();

        // Assert
        Assert.AreEqual(FirmwareUpdateServiceConfig.BootloaderHidTimeout, options.BootloaderResponseTimeout);
    }

    [TestMethod]
    public void CreateOptions_LeavesPostReconnectStaleHandleDelayAtCoreDefault()
    {
        // Regression guard for issue #738: do NOT zero PostReconnectStaleHandleDelay. Core's XML doc
        // suggests zeroing it on Windows, but our recorded successful bench flashes show the
        // close-and-reopen discard step running normally (it appears load-bearing here), and we have no
        // positive end-to-end validation that zeroing is safe. The #738 race is fixed at its source
        // (the desktop no longer races the port or tears down the flashing device during Core's
        // reconnect), not by narrowing this window — so leave the delay at Core's default.
        var coreDefault = new FirmwareUpdateServiceOptions().PostReconnectStaleHandleDelay;

        var options = FirmwareUpdateServiceConfig.CreateOptions();

        Assert.AreEqual(coreDefault, options.PostReconnectStaleHandleDelay);
        Assert.AreNotEqual(TimeSpan.Zero, options.PostReconnectStaleHandleDelay,
            "Do not zero this delay without positive hardware validation (issue #738).");
    }

    [TestMethod]
    public void CreateOptions_LeavesProgrammingTimeoutAtCoreDefault()
    {
        // Arrange - compare against a fresh Core default rather than a hard-coded value so this
        // stays green if Core changes its default; the intent is "desktop config does not override
        // ProgrammingTimeout", not "ProgrammingTimeout is exactly 10 min".
        var coreDefault = new FirmwareUpdateServiceOptions().ProgrammingTimeout;

        // Act - only the per-operation HID window is widened, not the overall programming budget.
        var options = FirmwareUpdateServiceConfig.CreateOptions();

        // Assert
        Assert.AreEqual(coreDefault, options.ProgrammingTimeout);
    }
}
