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

    /// <summary>
    /// Serial number of the device this notification belongs to, or <c>null</c> for application-level
    /// notices (e.g. the "new app version available" prompt). A null serial is the sentinel that keeps
    /// the notice from being pruned by device-disconnect cleanup.
    /// </summary>
    public string? DeviceSerialNo { get; init; }

    public required string Message { get; init; }

    /// <summary>
    /// Optional URL associated with the notification; null when the notification is not actionable.
    /// </summary>
    public string? Link { get; set; }
}