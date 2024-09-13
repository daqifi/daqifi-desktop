using Daqifi.Desktop.Channel;
using Daqifi.Desktop.DataModel.Channel;
using Daqifi.Desktop.IO.Messages.Consumers;
using Daqifi.Desktop.IO.Messages.Producers;
using System.Collections.Generic;
using Daqifi.Desktop.DataModel.Network;

namespace Daqifi.Desktop.Device
{
    public interface IStreamingDevice : IDevice
    {
        string DevicePartNumber { get; }
        NetworkConfiguration NetworkConfiguration { get; }
        List<string> AdcRanges { get; }
        string AdcRangeText { get; set; }
        string MacAddress { get; set; }
        string DeviceSerialNo { get; set; }
        int StreamingFrequency { get; set; }
        IMessageConsumer MessageConsumer { get; set; }
        IMessageProducer MessageProducer { get; set; }
        List<IChannel> DataChannels { get; set; }

        void InitializeStreaming();
        void StopStreaming();

        /// <summary>
        /// Sends a command to get any initialization data from the streamingDevice that might be needed
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
        /// Sends a command to activate a channel on the streamingDevice
        /// </summary>
        void AddChannel(IChannel channel);

        /// <summary>
        /// Sends a command to deactivate a channel on the streamingDevice
        /// </summary>
        void RemoveChannel(IChannel channel);

        void SetChannelOutputValue(IChannel channel, double value);

        void SetChannelDirection(IChannel channel, ChannelDirection direction);

        void UpdateNetworkConfiguration();
    }
}