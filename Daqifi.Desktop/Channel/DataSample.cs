using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using ChannelDirection = Daqifi.Core.Channel.ChannelDirection;
using ChannelType = Daqifi.Core.Channel.ChannelType;
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

    /// <summary>
    /// Firmware-derived time since the previous message, in milliseconds.
    /// Computed from the hardware timer (<c>msg_time_stamp</c>) via <c>TimestampProcessor</c>,
    /// so it is immune to TCP jitter and reflects actual sample timing.
    /// Null when firmware delta is not available (e.g. SD card imports).
    /// </summary>
    [NotMapped]
    public double? FirmwareDeltaMs { get; init; }

    [Required]
    public LoggingSession LoggingSession { get; set; }
    #endregion

    #region Constructors
    public DataSample() { }

    public DataSample(IDevice streamingDevice, IChannel channel, DateTime timestamp, double value, double? firmwareDeltaMs = null)
    {
        DeviceName = streamingDevice.Name;
        DeviceSerialNo=channel.DeviceSerialNo;
        ChannelName = channel.Name;
        Type = channel.Type;
        Color = channel.ChannelColorBrush.ToString(CultureInfo.InvariantCulture);
        Value = value;
        TimestampTicks = timestamp.Ticks;
        FirmwareDeltaMs = firmwareDeltaMs;
    }
    #endregion

    #region Object overrides
    public override string ToString()
    {
        return ID.ToString(CultureInfo.InvariantCulture);
    }
    #endregion
}