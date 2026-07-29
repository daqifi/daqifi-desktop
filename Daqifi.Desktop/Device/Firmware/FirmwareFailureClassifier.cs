using Daqifi.Core.Firmware;

namespace Daqifi.Desktop.Device.Firmware;

/// <summary>
/// Which firmware image was being flashed when a <see cref="FirmwareUpdateException"/> was thrown.
/// <para>
/// Core reuses one <see cref="FirmwareUpdateState"/> machine for both images, so
/// <see cref="FirmwareUpdateState.Verifying"/> means two different things depending on the image:
/// CRC verification for the PIC32 and the post-flash serial reconnect for the WiFi module. Only the
/// caller knows which flash it was running, so the phase has to be supplied alongside the exception.
/// </para>
/// </summary>
public enum FirmwareFlashPhase
{
    /// <summary>
    /// The PIC32 application-firmware flash: erase → program → CRC-verify → jump to application.
    /// </summary>
    Pic32 = 0,

    /// <summary>
    /// The WINC1500 WiFi-module flash: external WINC tool → success-marker check → serial reconnect
    /// and LAN restore.
    /// </summary>
    WifiModule = 1
}

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
    /// <param name="phase">Which image was being flashed when it was thrown.</param>
    /// <returns>
    /// <c>true</c> if the flash itself succeeded and only the reconnect timed out; <c>false</c> for
    /// every genuine flash failure (which must keep the Error/Sentry path).
    /// </returns>
    /// <remarks>
    /// <para>
    /// The discriminator is <em>structural</em>, deliberately not a match on
    /// <see cref="FirmwareUpdateException.Operation"/>. That property carries whatever text Core
    /// passed to its last <c>TransitionToState</c> call ("Reconnecting device and restoring LAN
    /// configuration." for the WiFi reconnect, "Verifying flash contents via CRC." for the PIC32
    /// CRC pass) — free-form prose with no published constant, so matching it would silently stop
    /// working the first time Core reworded a log line. The flash phase plus the failed state is
    /// exact and needs no literal:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>PIC32</b> — <see cref="FirmwareUpdateState.JumpingToApp"/> is the LAST step and is entered
    /// only after erase + program + CRC-verify all succeeded, so the firmware is already installed.
    /// <see cref="FirmwareUpdateState.Verifying"/> on this image is the CRC check, a real failure
    /// ("the device's flash CRC did not match"), and is intentionally NOT downgraded.
    /// </description></item>
    /// <item><description>
    /// <b>WiFi module</b> — the WiFi flow enters <see cref="FirmwareUpdateState.Verifying"/> at
    /// exactly one place, and only after Core has confirmed the WINC flash tool printed its success
    /// marker; the state covers the reconnect + LAN restore alone. It never enters
    /// <see cref="FirmwareUpdateState.JumpingToApp"/> (there is no bootloader jump).
    /// </description></item>
    /// </list>
    /// <para>
    /// Verified against Daqifi.Core v1.3.0, the consumed package version
    /// (<c>FirmwareUpdateService.cs</c>: PIC32 CRC-verify and jump at the <c>ErasingFlash</c> →
    /// <c>JumpingToApp</c> sequence, WiFi success-marker check immediately followed by the
    /// <c>Verifying</c> transition and <c>WaitForSerialReconnectAsync</c>).
    /// </para>
    /// <para>
    /// <b>Upstream:</b> this classifier is a workaround, tracked as daqifi-core#398 (gap 4). Core
    /// knows at every throw site whether the image was written, but exposes no machine-readable way
    /// to say so — the only other discriminator available is a UI progress string that was never an
    /// API contract and could be reworded in any release without anyone noticing. Core's
    /// <c>BuildRecoveryGuidance</c> has the same root cause: it maps by <c>FailedState</c> alone, so
    /// Core itself already emits the PIC32 CRC text for a *successful* WiFi flash. Once Core lands a
    /// dedicated state or flag for the post-flash reconnect, the phase parameter and this type can
    /// be replaced by a direct check on that signal.
    /// </para>
    /// </remarks>
    public static bool IsPostFlashReconnectTimeout(FirmwareUpdateException exception, FirmwareFlashPhase phase)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return phase switch
        {
            FirmwareFlashPhase.Pic32 => exception.FailedState == FirmwareUpdateState.JumpingToApp,
            FirmwareFlashPhase.WifiModule => exception.FailedState == FirmwareUpdateState.Verifying,
            _ => false
        };
    }

    /// <summary>
    /// The user-facing message for a post-flash reconnect timeout: the firmware installed, and the
    /// device needs a power-cycle to finish the job.
    /// </summary>
    /// <param name="phase">Which image was being flashed.</param>
    /// <returns>The message to present, phrased for the image that was flashed.</returns>
    public static string BuildInstalledButNotReconnectedMessage(FirmwareFlashPhase phase)
    {
        return phase == FirmwareFlashPhase.WifiModule
            ? WIFI_INSTALLED_NO_RECONNECT_MESSAGE
            : PIC32_INSTALLED_NO_RECONNECT_MESSAGE;
    }
    #endregion
}
