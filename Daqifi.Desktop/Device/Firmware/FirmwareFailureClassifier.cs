using Daqifi.Core.Firmware;

namespace Daqifi.Desktop.Device.Firmware;

/// <summary>
/// Decides whether a Core <see cref="FirmwareUpdateException"/> describes a genuine flash failure or
/// a <em>post-flash reconnect timeout</em> — the firmware was fully written and verified and only the
/// device's return to normal serial operation timed out.
/// <para>
/// A post-flash reconnect timeout is a device/environmental condition a power-cycle finishes, not an
/// app defect: it is logged at Warning (no Sentry capture) and reported to the user as installed.
/// Issue #738 / PR #751 established that treatment for the PIC32 case; issue #776 extends it to the
/// symmetric WiFi-module case.
/// </para>
/// </summary>
public static class FirmwareFailureClassifier
{
    #region Constants
    /// <summary>
    /// Shown when the PIC32 flash finished (erase + program + CRC-verify all passed) but the device
    /// did not re-enumerate its serial port in time (issue #738).
    /// </summary>
    internal const string PIC32_INSTALLED_NO_RECONNECT_MESSAGE =
        "Firmware was installed successfully, but the device did not return to normal mode on its " +
        "own. Please power-cycle the device (unplug and replug its USB cable), then reconnect.";

    /// <summary>
    /// Shown when the WINC flash finished (the flash tool reported its success marker) but the device
    /// did not re-enumerate its serial port in time (issue #776).
    /// </summary>
    internal const string WIFI_INSTALLED_NO_RECONNECT_MESSAGE =
        "WiFi firmware was installed successfully, but the device did not return to normal mode on " +
        "its own. Please power-cycle the device (unplug and replug its USB cable), then reconnect.";
    #endregion

    #region Public Methods
    /// <summary>
    /// Returns <c>true</c> when <paramref name="exception"/> is a post-flash reconnect timeout: the
    /// firmware image was completely written and verified, and the failure is only that the device
    /// did not come back on its serial port in time.
    /// </summary>
    /// <param name="exception">The exception Core threw. Never null.</param>
    /// <returns>
    /// <c>true</c> if the flash itself succeeded and only the reconnect timed out; <c>false</c> for
    /// every genuine flash failure (which must keep the Error/Sentry path).
    /// </returns>
    /// <remarks>
    /// <para>
    /// The discriminator is <em>structural</em>, deliberately not a match on
    /// <see cref="FirmwareUpdateException.Operation"/>. That property carries whatever text Core
    /// passed to its last <c>TransitionToState</c> call — free-form prose with no published
    /// constant, so matching it would silently stop working the first time Core reworded a log line.
    /// The failed state alone is exact, because the two post-flash reconnect states are disjoint
    /// across the two flows:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>PIC32</b> — <see cref="FirmwareUpdateState.JumpingToApp"/> is the LAST step and is entered
    /// only after erase + program + CRC-verify all succeeded, so the firmware is already installed.
    /// <see cref="FirmwareUpdateState.Verifying"/> on this image is the CRC check, a real failure
    /// ("the device's flash CRC did not match"), and is intentionally NOT downgraded.
    /// </description></item>
    /// <item><description>
    /// <b>WiFi module</b> — <see cref="FirmwareUpdateState.ReconnectingAfterFlash"/> is entered at
    /// exactly one place, and only after Core has confirmed the WINC flash tool printed its success
    /// marker; the state covers the serial re-enumeration + LAN restore alone. The WiFi flow never
    /// enters <see cref="FirmwareUpdateState.JumpingToApp"/> (there is no bootloader jump), and the
    /// PIC32 flow never enters <c>ReconnectingAfterFlash</c>.
    /// </description></item>
    /// </list>
    /// <para>
    /// Verified against Daqifi.Core v1.4.0, the consumed package version:
    /// <c>Pic32FirmwareUpdater.cs</c> transitions to <c>JumpingToApp</c> after the CRC pass, and
    /// <c>WifiModuleUpdater.cs</c> transitions to <c>ReconnectingAfterFlash</c> immediately after the
    /// success-marker check, wrapping <c>WaitForSerialReconnectAsync</c> plus the LAN restore.
    /// <c>ReconnectingAfterFlash</c> appears nowhere else in Core.
    /// </para>
    /// <para>
    /// This retires the phase-keyed carve-out issue #776 landed: until Core v1.4.0 the WiFi reconnect
    /// shared <see cref="FirmwareUpdateState.Verifying"/> with the PIC32 CRC check, so only the
    /// caller — which knew which image it was flashing — could tell an installed-but-unreachable
    /// device from a bad flash (daqifi-core#398 gap 4).
    /// </para>
    /// </remarks>
    public static bool IsPostFlashReconnectTimeout(FirmwareUpdateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.FailedState is FirmwareUpdateState.JumpingToApp
            or FirmwareUpdateState.ReconnectingAfterFlash;
    }

    /// <summary>
    /// The user-facing message for a post-flash reconnect timeout: the firmware installed, and the
    /// device needs a power-cycle to finish the job.
    /// </summary>
    /// <param name="failedState">
    /// The state Core failed in. <see cref="FirmwareUpdateState.ReconnectingAfterFlash"/> is the WiFi
    /// module's post-flash reconnect; anything else reaching here is the PIC32's
    /// <see cref="FirmwareUpdateState.JumpingToApp"/>.
    /// </param>
    /// <returns>The message to present, phrased for the image that was flashed.</returns>
    public static string BuildInstalledButNotReconnectedMessage(FirmwareUpdateState failedState)
    {
        return failedState == FirmwareUpdateState.ReconnectingAfterFlash
            ? WIFI_INSTALLED_NO_RECONNECT_MESSAGE
            : PIC32_INSTALLED_NO_RECONNECT_MESSAGE;
    }
    #endregion
}
