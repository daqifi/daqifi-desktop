using System.ComponentModel.DataAnnotations;
using Daqifi.Desktop.DataModel.Channel;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Logger;

namespace Daqifi.Desktop.Channel;

public class DataSample
{
    #region Properties
    public int ID { get; init; }
    public int LoggingSessionID { get; set; }
    public double Value { get; set; }
    public long TimestampTicks { get; init; }
    public string DeviceName { get; init; }
    public string ChannelName { get; init; }
    public string DeviceSerialNo { get; init; }
    public string Color { get; init; }
    public ChannelType Type { get; init; }

    [Required]
    public LoggingSession LoggingSession { get; set; }
    #endregion

    #region Constructors
    public DataSample() { }

    public DataSample(IDevice streamingDevice, IChannel channel, DateTime timestamp, double value)
    {
        DeviceName = streamingDevice.Name;
        DeviceSerialNo=channel.DeviceSerialNo;
        ChannelName = channel.Name;
        Type = channel.Type;
        Color = channel.ChannelColorBrush.ToString();
        Value = value;
        TimestampTicks = timestamp.Ticks;
    }
    #endregion

    #region Object overrides
    public override string ToString()
    {
        return ID.ToString();
    }
    #endregion
}