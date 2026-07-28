using Daqifi.Core.Firmware;
using Daqifi.Desktop.Device.Firmware;

namespace Daqifi.Desktop.Test.Device.Firmware;

/// <summary>
/// Behavior contract for <see cref="FirmwareFailureClassifier"/>: which Core firmware-update
/// failures are post-flash reconnect timeouts (the image is installed; only the device's return to
/// normal serial operation timed out) and which are genuine flash failures.
/// <para>
/// The load-bearing case is that <see cref="FirmwareUpdateState.Verifying"/> is overloaded across
/// the two images — the PIC32 CRC check versus the WiFi module's post-flash reconnect — so it must
/// downgrade on one phase and stay an Error on the other.
/// </para>
/// </summary>
[TestClass]
public class FirmwareFailureClassifierTests
{
    #region Constants
    /// <summary>
    /// The <c>Operation</c> Core attaches to a PIC32 CRC-verify failure, verified against
    /// Daqifi.Core v1.3.0 (<c>src/Daqifi.Core/Firmware/FirmwareUpdateService.cs</c>, the
    /// <c>TransitionToState(Verifying, ...)</c> immediately before the CRC-verify step).
    /// </summary>
    private const string CORE_PIC32_CRC_VERIFY_OPERATION = "Verifying flash contents via CRC.";

    /// <summary>
    /// The <c>Operation</c> Core attaches to a WiFi post-flash reconnect timeout, verified against
    /// Daqifi.Core v1.3.0 (<c>src/Daqifi.Core/Firmware/FirmwareUpdateService.cs</c>, the
    /// <c>TransitionToState(Verifying, ...)</c> that follows the WINC success-marker check).
    /// <para>
    /// Note this is NOT <c>"reconnect serial transport after WiFi flash"</c>: that label is the
    /// <c>ExecuteWithStateTimeoutAsync</c> argument and reaches only the inner
    /// <see cref="TimeoutException"/>'s message. <c>FirmwareUpdateException.Operation</c> is
    /// assigned solely from <c>TransitionToState</c>. Recorded here so a Core bump has one obvious
    /// place to re-check — production code deliberately does not match on either string.
    /// </para>
    /// </summary>
    private const string CORE_WIFI_RECONNECT_OPERATION = "Reconnecting device and restoring LAN configuration.";
    #endregion

    #region Tests
    [TestMethod]
    public void IsPostFlashReconnectTimeout_Pic32JumpingToApp_IsDowngraded()
    {
        // The last PIC32 step: reached only after erase + program + CRC-verify all succeeded, so the
        // firmware is on the device and a power-cycle finishes the job (issue #738).
        var exception = new FirmwareUpdateException(
            FirmwareUpdateState.JumpingToApp,
            "Jumping to application firmware.",
            "State 'JumpingToApp' timed out.");

        Assert.IsTrue(FirmwareFailureClassifier.IsPostFlashReconnectTimeout(
            exception, FirmwareFlashPhase.Pic32));
    }

    [TestMethod]
    public void IsPostFlashReconnectTimeout_WifiVerifying_IsDowngraded()
    {
        // The WiFi flow enters Verifying only AFTER the WINC flash tool printed its success marker,
        // so a timeout here means the module was flashed and only the serial reconnect ran out of
        // time (issue #776).
        var exception = new FirmwareUpdateException(
            FirmwareUpdateState.Verifying,
            CORE_WIFI_RECONNECT_OPERATION,
            "Firmware update failed in state 'Verifying' while Reconnecting device and restoring LAN configuration.");

        Assert.IsTrue(FirmwareFailureClassifier.IsPostFlashReconnectTimeout(
            exception, FirmwareFlashPhase.WifiModule));
    }

    [TestMethod]
    public void IsPostFlashReconnectTimeout_Pic32CrcVerifying_IsNotDowngraded()
    {
        // The whole point of keying on the phase: the SAME state on the PIC32 is the flash CRC
        // check, a deterministic "the flash does not match the image" failure. Downgrading it would
        // tell a user with a bad flash that their firmware installed fine.
        var exception = new FirmwareUpdateException(
            FirmwareUpdateState.Verifying,
            CORE_PIC32_CRC_VERIFY_OPERATION,
            "Firmware update failed in state 'Verifying' while Verifying flash contents via CRC.");

        Assert.IsFalse(FirmwareFailureClassifier.IsPostFlashReconnectTimeout(
            exception, FirmwareFlashPhase.Pic32));
    }

    [TestMethod]
    [DataRow(FirmwareUpdateState.PreparingDevice)]
    [DataRow(FirmwareUpdateState.WaitingForBootloader)]
    [DataRow(FirmwareUpdateState.Connecting)]
    [DataRow(FirmwareUpdateState.ErasingFlash)]
    [DataRow(FirmwareUpdateState.Programming)]
    [DataRow(FirmwareUpdateState.Failed)]
    [DataRow(FirmwareUpdateState.CleaningUp)]
    [DataRow(FirmwareUpdateState.Recovered)]
    public void IsPostFlashReconnectTimeout_GenuineFailureStates_AreNotDowngraded(FirmwareUpdateState failedState)
    {
        // Every state that is not a post-flash reconnect keeps the Error/Sentry path on BOTH phases.
        var exception = new FirmwareUpdateException(failedState, "some operation", "Failure.");

        Assert.IsFalse(FirmwareFailureClassifier.IsPostFlashReconnectTimeout(
            exception, FirmwareFlashPhase.Pic32));
        Assert.IsFalse(FirmwareFailureClassifier.IsPostFlashReconnectTimeout(
            exception, FirmwareFlashPhase.WifiModule));
    }

    [TestMethod]
    public void IsPostFlashReconnectTimeout_WifiJumpingToApp_IsNotDowngraded()
    {
        // The WiFi flow has no bootloader jump, so JumpingToApp is unreachable there. If Core ever
        // did surface it on this phase we have no evidence the module was programmed — stay on the
        // Error path rather than guessing.
        var exception = new FirmwareUpdateException(
            FirmwareUpdateState.JumpingToApp,
            "Jumping to application firmware.",
            "State 'JumpingToApp' timed out.");

        Assert.IsFalse(FirmwareFailureClassifier.IsPostFlashReconnectTimeout(
            exception, FirmwareFlashPhase.WifiModule));
    }

    [TestMethod]
    public void IsPostFlashReconnectTimeout_NullException_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            FirmwareFailureClassifier.IsPostFlashReconnectTimeout(null!, FirmwareFlashPhase.Pic32));
    }

    [TestMethod]
    public void BuildInstalledButNotReconnectedMessage_TellsUserTheFirmwareInstalledAndToPowerCycle()
    {
        var pic32Message = FirmwareFailureClassifier.BuildInstalledButNotReconnectedMessage(
            FirmwareFlashPhase.Pic32);
        var wifiMessage = FirmwareFailureClassifier.BuildInstalledButNotReconnectedMessage(
            FirmwareFlashPhase.WifiModule);

        // Both say "installed" (not "failed") and name the recovery action.
        StringAssert.Contains(pic32Message, "installed successfully");
        StringAssert.Contains(pic32Message, "power-cycle");
        StringAssert.Contains(wifiMessage, "installed successfully");
        StringAssert.Contains(wifiMessage, "power-cycle");

        // The WiFi variant names the module so the user knows which image is on the device.
        StringAssert.Contains(wifiMessage, "WiFi firmware");
        Assert.AreNotEqual(pic32Message, wifiMessage);
    }
    #endregion
}
