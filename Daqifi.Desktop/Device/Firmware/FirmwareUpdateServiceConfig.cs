using Daqifi.Core.Communication.Transport;
using Daqifi.Core.Firmware;

namespace Daqifi.Desktop.Device.Firmware;

/// <summary>
/// Centralizes the HID timing configuration for PIC32 bootloader firmware updates so every
/// construction site (DI registration and fallback factories) shares one value.
/// </summary>
/// <remarks>
/// The PIC32 bootloader performs a long flash operation (region erase / bank finalize) on a late
/// record that can keep its HID endpoint busy for longer than Core's 10 s defaults. That trips the
/// update with an <c>IOException: HID write failed</c> (transport <see cref="IHidTransport.WriteTimeout"/>)
/// or a read <see cref="System.TimeoutException"/>
/// (<see cref="FirmwareUpdateServiceOptions.BootloaderResponseTimeout"/>) — issue #575. The
/// pre-Core desktop flasher used unbounded blocking HID I/O and waited it out; we restore
/// equivalent headroom by widening both knobs to 30 s. That value is hardware-tested on a bench
/// NQ1. The overall <see cref="FirmwareUpdateServiceOptions.ProgrammingTimeout"/> (10 min) is left
/// at the Core default and still caps the whole programming phase.
/// </remarks>
public static class FirmwareUpdateServiceConfig
{
    #region Constants
    /// <summary>
    /// Per-operation HID timeout for the PIC32 bootloader handshake. Applied to both the transport
    /// (<see cref="IHidTransport.WriteTimeout"/> / <see cref="IHidTransport.ReadTimeout"/>) and
    /// <see cref="FirmwareUpdateServiceOptions.BootloaderResponseTimeout"/> so the write and read
    /// paths share the same window. <c>WriteAsync</c> takes no timeout argument and always uses the
    /// transport's <see cref="IHidTransport.WriteTimeout"/>, while the service passes
    /// <see cref="FirmwareUpdateServiceOptions.BootloaderResponseTimeout"/> to every read — so both
    /// must be set to cover the failure.
    /// </summary>
    public static readonly TimeSpan BootloaderHidTimeout = TimeSpan.FromSeconds(30);
    #endregion

    #region Public Methods
    /// <summary>
    /// Creates a HID transport configured with <see cref="BootloaderHidTimeout"/> for both reads and writes.
    /// </summary>
    /// <returns>A <see cref="HidLibraryTransport"/> whose read and write timeouts are set to <see cref="BootloaderHidTimeout"/>.</returns>
    public static HidLibraryTransport CreateBootloaderHidTransport()
    {
        return new HidLibraryTransport
        {
            ReadTimeout = BootloaderHidTimeout,
            WriteTimeout = BootloaderHidTimeout,

            // A2 (stray-write guard): open the bootloader's HID handle exclusively and hold it for
            // the whole flash session so no other user-mode opener — the connection dialog's own HID
            // discovery loop, a second app instance, anything — can open or write to the device
            // mid-flash. The PIC32 bootloader's CRC check is disabled, so a stray frame from another
            // opener can be mis-parsed as an ERASE; the exclusive handle is the guard. Best-effort in
            // Core: a refused exclusive open falls back to shared so a working flash is not regressed.
            ExclusiveAccess = true
        };
    }

    /// <summary>
    /// Creates the firmware update options for PIC32 updates with the widened bootloader response timeout.
    /// </summary>
    /// <returns>
    /// A <see cref="FirmwareUpdateServiceOptions"/> with <see cref="FirmwareUpdateServiceOptions.BootloaderResponseTimeout"/>
    /// set to <see cref="BootloaderHidTimeout"/>; all other options retain their Core defaults.
    /// </returns>
    /// <remarks>
    /// <para>
    /// NOTE — <see cref="FirmwareUpdateServiceOptions.PostReconnectStaleHandleDelay"/> is deliberately
    /// LEFT at Core's non-zero default. Core documents it as a macOS-only workaround ("set to zero on
    /// Windows, where the first open is already clean"), and issue #738 tempted us to zero it to shrink
    /// the post-reset reconnect window. We do NOT: the assumption that the Windows first open is clean is
    /// unverified on our hardware, and the successful bench flashes we have on record show Core's
    /// close-and-reopen discard step running normally — i.e. it appears load-bearing here. Changing
    /// firmware-flash reconnect timing without a positive end-to-end hardware validation is not worth the
    /// marginal window reduction. The #738 race is fixed at its source (the desktop no longer steals the
    /// port or tears down the flashing device during Core's reconnect — see
    /// <c>ConnectionManager.IsFirmwareUpdateInProgress</c> and <c>ConnectionManager.IsDeviceBeingUpdated</c>),
    /// not by narrowing this window.
    /// </para>
    /// </remarks>
    public static FirmwareUpdateServiceOptions CreateOptions()
    {
        return new FirmwareUpdateServiceOptions
        {
            BootloaderResponseTimeout = BootloaderHidTimeout
        };
    }
    #endregion
}
