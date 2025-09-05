namespace Daqifi.Desktop.DataModel.Device;

public class DeviceInfo
{
    public string DeviceName { get; init; }

    public string IpAddress { get; init; }

    public string MacAddress { get; init; }

    public uint Port { get; init; }

    public bool IsPowerOn { get; set; }

    public string DeviceSerialNo { get; init; }

    public string DeviceVersion { get; init; }
}