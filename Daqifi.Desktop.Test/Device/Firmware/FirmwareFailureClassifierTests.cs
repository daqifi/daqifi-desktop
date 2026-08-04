using Daqifi.Core.Firmware;
using Daqifi.Desktop.Device.Firmware;

namespace Daqifi.Desktop.Test.Device.Firmware;

/// <summary>
/// Behavior contract for <see cref="FirmwareFailureClassifier"/>: which Core firmware-update
/// failures are post-flash reconnect timeouts (the image is installed; only the device's return to
/// normal serial operation timed out) and which are genuine flash failures.
/// <para>
/// The load-bearing case is that <see cref="FirmwareUpdateState.Verifying"/> must stay a genuine
/// failure while the two post-flash reconnect states —
/// <see cref="FirmwareUpdateState.JumpingToApp"/> on the PIC32 and
/// <see cref="FirmwareUpdateState.ReconnectingAfterFlash"/> on the WiFi module — are downgraded.
/// Core v1.4.0 split those apart (daqifi-core#398 gap 4); before it, the WiFi reconnect shared
/// <c>Verifying</c> with the PIC32 CRC check and only the caller's flash phase could tell them
/// apart (issue #776).
/// </para>
/// </summary>
[TestClass]
public class FirmwareFailureClassifierTests
{
    #region Constants
    /// <summary>
    /// The <c>Operation</c> Core attaches to a PIC32 CRC-verify failure, verified against
    /// Daqifi.Core v1.4.0 (<c>src/Daqifi.Core/Firmware/Pic32BootloaderSession.cs</c>, the
    /// <c>Verifying</c> transition around the CRC-verify step). Recorded so a Core bump has one
    /// obvious place to re-check — production code deliberately does not match on it.
    /// </summary>
    private const string CORE_PIC32_CRC_VERIFY_OPERATION = "Verifying flash contents via CRC.";

    /// <summary>
    /// The <c>Operation</c> Core attaches to a WiFi post-flash reconnect timeout, verified against
    /// Daqifi.Core v1.4.0 (<c>src/Daqifi.Core/Firmware/WifiModuleUpdater.cs</c>, the
    /// <c>TransitionToState(ReconnectingAfterFlash, ...)</c> that follows the WINC success-marker
    /// check). As above: recorded, not matched on.
    /// </summary>
    private const string CORE_WIFI_RECONNECT_OPERATION = "Reconnecting device and restoring LAN configuration.";
    #endregion

    #region Tests
    [TestMethod]
    public void IsPostFlashReconnectTimeout_Pic32JumpingToApp_IsDowngraded()
    {
        // The last PIC32 step: reached only after erase + program + CRC-verify all succeeded, so the
        // firmware is on the device and a power-cycle finishes the job (issue #738).
        // Arrange
        var exception = new FirmwareUpdateException(
            FirmwareUpdateState.JumpingToApp,
            "Jumping to application firmware.",
            "State 'JumpingToApp' timed out.");

        // Act
        var isDowngraded = FirmwareFailureClassifier.IsPostFlashReconnectTimeout(exception);

        // Assert
        Assert.IsTrue(isDowngraded);
    }

    [TestMethod]
    public void IsPostFlashReconnectTimeout_WifiReconnectingAfterFlash_IsDowngraded()
    {
        // Core v1.4.0 enters this state only AFTER the WINC flash tool printed its success marker,
        // so a timeout here means the module was flashed and only the serial re-enumeration ran out
        // of time (issue #776, daqifi-core#398 gap 4).
        // Arrange
        var exception = new FirmwareUpdateException(
            FirmwareUpdateState.ReconnectingAfterFlash,
            CORE_WIFI_RECONNECT_OPERATION,
            "Firmware update failed in state 'ReconnectingAfterFlash' while Reconnecting device and " +
            "restoring LAN configuration.");

        // Act
        var isDowngraded = FirmwareFailureClassifier.IsPostFlashReconnectTimeout(exception);

        // Assert
        Assert.IsTrue(isDowngraded);
    }

    [TestMethod]
    public void IsPostFlashReconnectTimeout_CrcVerifying_IsNotDowngraded()
    {
        // The regression this whole classifier exists to prevent: Verifying is now unambiguously the
        // PIC32 flash CRC check, a deterministic "the flash does not match the image" failure.
        // Downgrading it would tell a user with a bad flash that their firmware installed fine.
        // Arrange
        var exception = new FirmwareUpdateException(
            FirmwareUpdateState.Verifying,
            CORE_PIC32_CRC_VERIFY_OPERATION,
            "Firmware update failed in state 'Verifying' while Verifying flash contents via CRC.");

        // Act
        var isDowngraded = FirmwareFailureClassifier.IsPostFlashReconnectTimeout(exception);

        // Assert
        Assert.IsFalse(isDowngraded);
    }

    [TestMethod]
    [DataRow(FirmwareUpdateState.Idle)]
    [DataRow(FirmwareUpdateState.PreparingDevice)]
    [DataRow(FirmwareUpdateState.WaitingForBootloader)]
    [DataRow(FirmwareUpdateState.Connecting)]
    [DataRow(FirmwareUpdateState.ErasingFlash)]
    [DataRow(FirmwareUpdateState.Programming)]
    [DataRow(FirmwareUpdateState.Verifying)]
    [DataRow(FirmwareUpdateState.Complete)]
    [DataRow(FirmwareUpdateState.Failed)]
    [DataRow(FirmwareUpdateState.CleaningUp)]
    [DataRow(FirmwareUpdateState.Recovered)]
    public void IsPostFlashReconnectTimeout_EveryOtherState_IsNotDowngraded(
        FirmwareUpdateState failedState)
    {
        // Exhaustive over FirmwareUpdateState as of Core v1.4.0: every member except the two
        // post-flash reconnect states keeps the Error/Sentry path. A new Core state arriving without
        // a deliberate decision here shows up as a compile-time gap in this list, not as a silent
        // downgrade.
        // Arrange
        var exception = new FirmwareUpdateException(failedState, "some operation", "Failure.");

        // Act
        var isDowngraded = FirmwareFailureClassifier.IsPostFlashReconnectTimeout(exception);

        // Assert
        Assert.IsFalse(isDowngraded);
    }

    [TestMethod]
    public void IsPostFlashReconnectTimeout_CoversEveryCoreState()
    {
        // Guards the exhaustiveness the DataRows above claim: if Core adds a state, this fails and
        // forces a decision about which side of the classifier it belongs on.
        var downgraded = new[]
        {
            FirmwareUpdateState.JumpingToApp,
            FirmwareUpdateState.ReconnectingAfterFlash
        };
        var notDowngraded = new[]
        {
            FirmwareUpdateState.Idle,
            FirmwareUpdateState.PreparingDevice,
            FirmwareUpdateState.WaitingForBootloader,
            FirmwareUpdateState.Connecting,
            FirmwareUpdateState.ErasingFlash,
            FirmwareUpdateState.Programming,
            FirmwareUpdateState.Verifying,
            FirmwareUpdateState.Complete,
            FirmwareUpdateState.Failed,
            FirmwareUpdateState.CleaningUp,
            FirmwareUpdateState.Recovered
        };

        CollectionAssert.AreEquivalent(
            Enum.GetValues<FirmwareUpdateState>(),
            downgraded.Concat(notDowngraded).ToArray(),
            "Core gained or lost a FirmwareUpdateState. Decide whether the new one is a post-flash " +
            "reconnect (the image is installed) or a genuine failure, then update both lists.");
    }

    [TestMethod]
    public void IsPostFlashReconnectTimeout_NullException_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            FirmwareFailureClassifier.IsPostFlashReconnectTimeout(null!));
    }

    // The expected opening is state-specific and case-sensitive, which is also what proves the two
    // messages are distinct: the WiFi text reads "WiFi firmware was installed successfully", so it
    // cannot satisfy the PIC32 row's capital-F "Firmware was installed successfully" and vice versa.
    // A separate distinctness assertion would need a second Act, so this carries that guarantee.
    [TestMethod]
    [DataRow(FirmwareUpdateState.JumpingToApp, "Firmware was installed successfully")]
    [DataRow(FirmwareUpdateState.ReconnectingAfterFlash, "WiFi firmware was installed successfully")]
    public void BuildInstalledButNotReconnectedMessage_TellsUserTheFirmwareInstalledAndToPowerCycle(
        FirmwareUpdateState failedState, string expectedOpening)
    {
        var message = FirmwareFailureClassifier.BuildInstalledButNotReconnectedMessage(failedState);

        // Says "installed" (never "failed") and names the recovery action.
        StringAssert.Contains(message, expectedOpening);
        StringAssert.Contains(message, "power-cycle");
        Assert.IsFalse(
            message.Contains("failed", StringComparison.OrdinalIgnoreCase),
            "The firmware installed; the message must not tell the user it failed.");
    }
    #endregion
}
