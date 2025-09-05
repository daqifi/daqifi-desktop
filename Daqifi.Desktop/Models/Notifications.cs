namespace Daqifi.Desktop.Models;

public class Notifications
{
    public bool IsFirmwareUpdate { get; init; }

    public string DeviceSerialNo { get; init; }

    public string Message { get; init; }

    public string Link { get; set; }
}