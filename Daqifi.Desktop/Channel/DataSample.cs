using System;
using System.ComponentModel.DataAnnotations;
using Daqifi.Desktop.DataModel.Channel;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Logger;

namespace Daqifi.Desktop.Channel
{
    public class DataSample
    {
        #region Properties
        public int ID { get; set; }
        public int LoggingSessionID { get; set; }
        public double Value { get; set; }
        public long TimestampTicks { get; set; }
        public string DeviceName { get; set; }
        public string ChannelName { get; set; }
        public string Color { get; set; }
        public ChannelType Type { get; set; }

        [Required]
        public LoggingSession LoggingSession { get; set; }
        #endregion

        #region Constructors
        public DataSample() { }

        public DataSample(IDevice streamingDevice, IChannel channel, DateTime timestamp, double value)
        {
            DeviceName = streamingDevice.Name;
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
}
