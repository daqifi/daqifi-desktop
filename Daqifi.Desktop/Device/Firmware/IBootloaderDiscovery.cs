namespace Daqifi.Desktop.Device.Firmware;

/// <summary>
/// Identifies a HID bootloader that discovery surfaced.
/// </summary>
public sealed class BootloaderDiscoveredEventArgs : EventArgs
{
    /// <summary>Creates the event args.</summary>
    public BootloaderDiscoveredEventArgs(string devicePath, string? deviceName)
    {
        DevicePath = devicePath;
        DeviceName = deviceName;
    }

    /// <summary>OS HID device path of the discovered bootloader.</summary>
    public string DevicePath { get; }

    /// <summary>Friendly device name, when discovery could read it; otherwise null.</summary>
    public string? DeviceName { get; }
}

/// <summary>
/// Continuous HID-bootloader discovery source for the <see cref="IBootloaderWatcher"/>. Abstracted from
/// Core's <c>HidDeviceFinder</c> so the watcher can be unit-tested without hardware (tests raise
/// <see cref="BootloaderDiscovered"/> directly) and so discovery can be paused around a flash.
/// </summary>
public interface IBootloaderDiscovery
{
    /// <summary>Raised once per discovery cycle for every matching HID bootloader currently present.</summary>
    event EventHandler<BootloaderDiscoveredEventArgs>? BootloaderDiscovered;

    /// <summary>Starts (or resumes) the continuous discovery loop. Idempotent while running.</summary>
    void Start();

    /// <summary>
    /// Stops the discovery loop. Used to pause discovery for the duration of a flash so it does not
    /// re-open or fight the bootloader's HID I/O. <see cref="Start"/> resumes afterward.
    /// </summary>
    void Stop();
}
