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
