namespace Daqifi.Desktop.Device;

/// <summary>
/// Raised when a streaming device's underlying Core connection drops unexpectedly (reboot,
/// USB/CDC unplug, WiFi/TCP drop, firmware-flash re-enumeration, HID disconnect) rather than
/// through an explicit, desktop-initiated <see cref="IDevice.Disconnect"/> call.
/// </summary>
public class ConnectionLostEventArgs(string reason) : EventArgs
{
    /// <summary>Short, human-readable description of why the connection was lost.</summary>
    public string Reason { get; } = reason;
}
