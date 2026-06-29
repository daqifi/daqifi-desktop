namespace Daqifi.Desktop.Device.Firmware;

/// <summary>
/// A sitting HID bootloader the <see cref="IBootloaderWatcher"/> is holding (handle open, keep-alive
/// read pending) so Windows USB selective-suspend can't wedge it (daqifi-nyquist-firmware#568). Bound to
/// the connection dialog's firmware list; the user picks one to flash. Identified by its OS HID device
/// path — identical bootloaders share VID/PID and have no serial, so the path is the only discriminator.
/// </summary>
public sealed class HeldBootloader
{
    /// <summary>Creates a held-bootloader list item.</summary>
    /// <param name="devicePath">OS HID device path — the stable identity used to target the flash.</param>
    /// <param name="displayName">Friendly name shown in the UI.</param>
    public HeldBootloader(string devicePath, string displayName)
    {
        DevicePath = string.IsNullOrWhiteSpace(devicePath)
            ? throw new ArgumentException("Device path cannot be empty.", nameof(devicePath))
            : devicePath;
        DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? throw new ArgumentException("Display name cannot be empty.", nameof(displayName))
            : displayName;
    }

    /// <summary>OS HID device path; the stable identity passed to the path-targeted flash.</summary>
    public string DevicePath { get; }

    /// <summary>Friendly name shown in the connection dialog's firmware list.</summary>
    public string DisplayName { get; }
}
