namespace Daqifi.Desktop.DataModel.Device;

public class DeviceInfo
{
    public string DeviceName { get; set; }

    public string IpAddress { get; set; }

    public string MacAddress { get; set; }

    public uint Port { get; set; }

    public bool IsPowerOn { get; set; }

    public string DeviceSerialNo { get; set; }

    public string DeviceVersion { get; set; }
}