namespace Daqifi.Desktop.Device.Firmware;

/// <summary>
/// Grabs and holds a sitting PIC32 HID bootloader's USB handle so it cannot be wedged by Windows USB
/// selective-suspend before the user flashes it (daqifi-nyquist-firmware#568).
/// <para>
/// The wedge happens when the host lets an idle bootloader go to selective-suspend and then reopens it:
/// the reopen is a bus RESET with no follow-up SET_CONFIGURATION, so the device never reaches
/// <c>USB_DEVICE_EVENT_CONFIGURED</c>, its data endpoints stay dark, and the version handshake times
/// out. The cure is to never let it suspend in the first place: hold the handle open with an
/// interrupt-IN read continuously pending (an <em>idle</em> open handle alone does not stop suspend — a
/// pending read IRP does). Reads carry no payload to the device, so unlike a write they can never be
/// mis-parsed as a stray command — they are the one form of I/O safe to direct at a sitting bootloader.
/// </para>
/// <para>
/// The hold uses the same shared exclusive <c>IHidTransport</c> the flasher uses, so handing off to the
/// flash is seamless: the flasher reopens the already-warm handle with no idle gap.
/// </para>
/// </summary>
public interface IBootloaderHoldService
{
    /// <summary>Whether a hold is currently active (handle open, keep-alive read pending).</summary>
    bool IsHolding { get; }

    /// <summary>
    /// Opens the bootloader's HID handle and starts the keep-alive read loop. Best-effort and
    /// idempotent: if no bootloader is present (e.g. a just-flashed device now in application mode) the
    /// open fails and the call returns without holding and without throwing. A no-op if already holding.
    /// </summary>
    Task BeginHoldAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the keep-alive read so the flasher can own the device's HID I/O, but leaves the handle
    /// OPEN and warm for the flasher to reopen with no idle gap. Lets the in-flight read drain fully so
    /// no orphaned read IRP can swallow the flasher's first response. Call immediately before flashing.
    /// </summary>
    Task PauseForFlashAsync();

    /// <summary>
    /// Stops the keep-alive read and closes the handle. Call when the device is no longer being managed
    /// (the connection dialog closes) so the bootloader is never left held.
    /// </summary>
    Task ReleaseAsync();
}
