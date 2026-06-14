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
    public void CreateOptions_SetsBootloaderResponseTimeout()
    {
        // Act - the service passes BootloaderResponseTimeout to every bootloader read.
        var options = FirmwareUpdateServiceConfig.CreateOptions();

        // Assert
        Assert.AreEqual(FirmwareUpdateServiceConfig.BootloaderHidTimeout, options.BootloaderResponseTimeout);
    }

    [TestMethod]
    public void CreateOptions_LeavesProgrammingTimeoutAtCoreDefault()
    {
        // Act - the overall programming-phase budget should remain the Core default (10 min);
        // only the per-operation HID window is widened.
        var options = FirmwareUpdateServiceConfig.CreateOptions();

        // Assert
        Assert.AreEqual(TimeSpan.FromMinutes(10), options.ProgrammingTimeout);
    }
}
