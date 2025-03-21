namespace Daqifi.Desktop.Device;

public class DeviceMessage
{
    public int LoggingSessionID { get; set; }
    public long TimestampTicks { get; set; }
    public long AppTicks { get; set; }
    public string DeviceName { get; set; }
    public string DeviceSerialNo { get; set; }

    public string DeviceVersion { get; set; }

    public int DigitalChannelCount { get; set; }
    public int AnalogChannelCount { get; set; }
    public int DeviceStatus { get; set; }
    public int PowerStatus { get; set; }
    public int BatteryStatus { get; set; }
    public int TempStatus { get; set; }
    public int TargetFrequency { get; set; }
    public bool Rollover { get; set; }

}