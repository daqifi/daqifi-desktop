namespace Daqifi.Desktop.Models;

public class Notifications
{
    public bool IsFirmwareUpdate { get; init; }

    /// <summary>
    /// True when this notification is the "WiFi module firmware is out of date / unreadable" prompt
    /// (distinct from the device firmware update flagged by <see cref="IsFirmwareUpdate"/>). Used to
    /// de-duplicate and clear WiFi-specific notifications per device.
    /// </summary>
    public bool IsWifiFirmwareUpdate { get; init; }

    public string DeviceSerialNo { get; init; }

    public string Message { get; init; }

    public string Link { get; set; }
}