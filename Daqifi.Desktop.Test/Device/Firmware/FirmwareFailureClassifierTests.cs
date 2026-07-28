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
    // See daqifi-core#398 (gap 4) — the upstream ask to replace these unversioned progress strings
    // with a real discriminator. Until that lands, this is the ONE place to re-check on a Core bump.

    /// <summary>
    /// The <c>Operation</c> Core attaches to a PIC32 CRC-verify failure, verified against
    /// Daqifi.Core v1.3.0 (<c>src/Daqifi.Core/Firmware/FirmwareUpdateService.cs</c>, the
    /// <c>TransitionToState(Verifying, ...)</c> immediately before the CRC-verify step).
    /// See daqifi-core#398 (gap 4).
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
    /// See daqifi-core#398 (gap 4).
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
    public void IsPostFlashReconnectTimeout_Pic32GenuineFailureStates_AreNotDowngraded(
        FirmwareUpdateState failedState)
    {
        // Every PIC32 state that is not the JumpingToApp reconnect keeps the Error/Sentry path.
        var exception = new FirmwareUpdateException(failedState, "some operation", "Failure.");

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
    public void IsPostFlashReconnectTimeout_WifiGenuineFailureStates_AreNotDowngraded(
        FirmwareUpdateState failedState)
    {
        // Every WiFi state that is not the Verifying reconnect keeps the Error/Sentry path.
        var exception = new FirmwareUpdateException(failedState, "some operation", "Failure.");

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

    // The expected opening is phase-specific and case-sensitive, which is also what proves the two
    // messages are distinct: the WiFi text reads "WiFi firmware was installed successfully", so it
    // cannot satisfy the PIC32 row's capital-F "Firmware was installed successfully" and vice versa.
    // A separate distinctness assertion would need a second Act, so this carries that guarantee.
    [TestMethod]
    [DataRow(FirmwareFlashPhase.Pic32, "Firmware was installed successfully")]
    [DataRow(FirmwareFlashPhase.WifiModule, "WiFi firmware was installed successfully")]
    public void BuildInstalledButNotReconnectedMessage_TellsUserTheFirmwareInstalledAndToPowerCycle(
        FirmwareFlashPhase phase, string expectedOpening)
    {
        var message = FirmwareFailureClassifier.BuildInstalledButNotReconnectedMessage(phase);

        // Says "installed" (never "failed") and names the recovery action.
        StringAssert.Contains(message, expectedOpening);
        StringAssert.Contains(message, "power-cycle");
        Assert.IsFalse(
            message.Contains("failed", StringComparison.OrdinalIgnoreCase),
            "The firmware installed; the message must not tell the user it failed.");
    }
    #endregion
}
