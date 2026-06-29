using System.Collections.ObjectModel;

namespace Daqifi.Desktop.Device.Firmware;

/// <summary>Identifies a held bootloader that dropped out of the watcher's list.</summary>
public sealed class BootloaderHoldDroppedEventArgs : EventArgs
{
    /// <summary>Creates the event args.</summary>
    public BootloaderHoldDroppedEventArgs(string devicePath)
    {
        DevicePath = devicePath;
    }

    /// <summary>OS HID device path of the bootloader whose hold was dropped.</summary>
    public string DevicePath { get; }
}

/// <summary>
/// App-global service that discovers <em>every</em> sitting PIC32 HID bootloader and holds each one's USB
/// handle open with a keep-alive read so Windows USB selective-suspend can't wedge it before the user
/// flashes (daqifi-nyquist-firmware#568). Each bootloader is held over its own exclusive transport,
/// addressed by device path (identical bootloaders share VID/PID and have no serial), so holding or
/// flashing one never disturbs the others.
/// <para>
/// Holds persist for the app's lifetime regardless of whether the connection dialog is open — that is the
/// point of an app-global watcher. The dialog binds to <see cref="Bootloaders"/> to list them and calls
/// <see cref="PrepareFlashAsync"/> to flash one; the auto-update coordinator calls
/// <see cref="SuspendDiscoveryAsync"/> so the watcher doesn't grab the device it is mid-flashing.
/// </para>
/// </summary>
public interface IBootloaderWatcher
{
    /// <summary>
    /// The bootloaders currently held, bound to the connection dialog's firmware list. Mutated on the UI
    /// thread so it can be data-bound directly.
    /// </summary>
    ObservableCollection<HeldBootloader> Bootloaders { get; }

    /// <summary>Raised when a held bootloader drops off the list (device removed / surprise-detached).</summary>
    event EventHandler<BootloaderHoldDroppedEventArgs>? HoldDropped;

    /// <summary>Begins discovery and holding. Idempotent; call once at app startup.</summary>
    void Start();

    /// <summary>
    /// Prepares to flash one specific held bootloader: pauses discovery and releases <em>only</em> that
    /// device's hold (so the flasher can open it by path), while every other bootloader stays held and
    /// wedge-proof. Disposing the returned lease re-grabs the target (if it is still a bootloader — a
    /// successful flash leaves it in application mode, so it drops off the list) and resumes discovery.
    /// </summary>
    /// <param name="devicePath">OS HID device path of the bootloader to flash.</param>
    /// <returns>A lease whose disposal restores the hold and resumes discovery.</returns>
    Task<IAsyncDisposable> PrepareFlashAsync(string devicePath);

    /// <summary>
    /// Suspends discovery (and suppresses new grabs) for the duration of an auto-update, while keeping
    /// existing holds alive. Used by the firmware coordinator: when a connected device reboots into the
    /// bootloader mid-update, the watcher must not grab it out from under the coordinator's flasher.
    /// Disposing the returned lease resumes discovery.
    /// </summary>
    /// <returns>A lease whose disposal resumes discovery.</returns>
    Task<IAsyncDisposable> SuspendDiscoveryAsync();
}
