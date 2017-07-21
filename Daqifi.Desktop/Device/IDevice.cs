using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Message;
using System.Collections.Generic;
using System.ComponentModel;

namespace Daqifi.Desktop.Device
{
    public interface IDevice : INotifyPropertyChanged
    {
        int Id { get; set; }
        string Name { get; set; }
        string AdcRangeText { get; set; }
        int StreamingFrequency { get; set; }
        IMessageConsumer MessageConsumer { get; set; }
        IMessageProducer MessageProducer { get; set; }
        List<IChannel> DataChannels { get; set; }

        /// <summary>
        /// Connects to the device.
        /// </summary>
        /// <returns>True if successfully connected</returns>
        bool Connect();

        /// <summary>
        /// Disconnects from the device
        /// </summary>
        /// <returns>True if successfully disconnected</returns>
        bool Disconnect();

        void InitializeStreaming();
        void StopStreaming();

        /// <summary>
        /// Sends a command to get any intialization data from the device that might be needed
        /// </summary>
        void InitializeDeviceState();

        /// <summary>
        /// Sets the ADC mode. Either "Single Ended" or "Differential"
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="mode">ADC Mode</param>
        void SetAdcMode(IChannel channel, AdcMode mode);

        /// <summary>
        ///  Sets the ADC Range
        /// </summary>
        /// <param name="range">ADC Range (Plus or Minus)</param>
        void SetAdcRange(int range);

        /// <summary>
        /// Sends a command to active a channel on the device
        /// </summary>
        void AddChannel(IChannel channel);

        /// <summary>
        /// Sends a command to deactive a channel on the device
        /// </summary>
        void RemoveChannel(IChannel channel);

        void SetChannelOutputValue(IChannel channel, double value);

        void SetChannelDirection(IChannel channel, ChannelDirection direction);

        void UpdateNetworkConfiguration();

        void UpdateFirmware(byte[] data);

        /// <summary>
        /// Reboots the device
        /// </summary>
        void Reboot();
    }
}
