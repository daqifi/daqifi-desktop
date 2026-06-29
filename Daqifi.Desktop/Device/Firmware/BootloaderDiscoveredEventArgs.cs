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
