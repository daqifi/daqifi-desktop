namespace Daqifi.Desktop.Device.Firmware;

/// <summary>
/// Identifies a HID bootloader that discovery surfaced.
/// </summary>
public sealed class BootloaderDiscoveredEventArgs : EventArgs
{
    /// <summary>Creates the event args.</summary>
    /// <param name="devicePath">OS HID device path of the discovered bootloader.</param>
    /// <param name="deviceName">Friendly device name, when discovery could read it; otherwise null.</param>
    /// <param name="locationKey">USB physical-location key, when discovery could resolve one; otherwise null.</param>
    public BootloaderDiscoveredEventArgs(string devicePath, string? deviceName, string? locationKey = null)
    {
        DevicePath = string.IsNullOrWhiteSpace(devicePath)
            ? throw new ArgumentException("Device path cannot be empty.", nameof(devicePath))
            : devicePath;
        DeviceName = deviceName;
        LocationKey = locationKey;
    }

    /// <summary>OS HID device path of the discovered bootloader.</summary>
    public string DevicePath { get; }

    /// <summary>Friendly device name, when discovery could read it; otherwise null.</summary>
    public string? DeviceName { get; }

    /// <summary>
    /// USB physical-location key (e.g. <c>Port_#0001.Hub_#0001</c>), when Core could resolve one for
    /// this HID device path; otherwise null. Stable across serial ⇄ HID-bootloader mode transitions on
    /// the same physical device, so it can correlate this bootloader with the device it used to be.
    /// </summary>
    public string? LocationKey { get; }
}
