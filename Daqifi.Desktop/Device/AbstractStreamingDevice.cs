using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Communication.Protobuf;
using Daqifi.Desktop.DataModel.Channel;
using Daqifi.Desktop.DataModel.Network;
using Daqifi.Desktop.Loggers;
using Daqifi.Desktop.Message;
using Daqifi.Desktop.Message.Consumers;
using Daqifi.Desktop.Message.MessageTypes;
using Daqifi.Desktop.Message.Producers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Daqifi.Desktop.Device
{
    public abstract class AbstractStreamingDevice : ObservableObject, IStreamingDevice
    {
        #region Private Data

        protected static DateTime? _previousTimestamp;
        private string _adcRangeText;
        protected readonly double AdcResolution = 131072;
        protected double AdcRange = 1;
        private int _streamingFrequency = 1;
        private uint _timestampFrequency;
        private uint? _previousDeviceTimestamp;
        #endregion

        #region Properties
        public AppLogger AppLogger = AppLogger.Instance;
        private IList<float> _analogInPortRanges;

        public int Id { get; set; }

        public string Name { get; set; }

        public int StreamingFrequency
        {
            get => _streamingFrequency;
            set
            {
                if (value < 1) return;
                _streamingFrequency = value;
                NotifyPropertyChanged("StreamingFrequency");
            }
        }

        public List<string> Modes { get; } = new List<string> { "Self-Hosted", "Existing Network"};
        public List<string> SecurityTypes { get; } = new List<string>();
        public List<string> AdcRanges { get; } = new List<string> { "+/-5V", "+/-10V" };

        public NetworkConfiguration NetworkConfiguration { get; set; } = new NetworkConfiguration();

        public IMessageConsumer MessageConsumer { get; set; }
        public IMessageProducer MessageProducer { get; set; }
        public List<IChannel> DataChannels { get; set; }

        public string AdcRangeText
        {
            get => _adcRangeText;
            set 
            {
                if(value == AdcRanges[0])
                {
                    SetAdcRange(5);
                }
                else if (value == AdcRanges[1])
                {
                    SetAdcRange(10);
                }
                else
                {
                    return;
                }
                _adcRangeText = value;
                NotifyPropertyChanged("AdcRange");
            }
        }

        public bool IsStreaming { get; set; }

        #endregion

        #region Abstract Methods
        public abstract bool Connect();

        public abstract bool Disconnect();
        #endregion

        #region Message Handlers
        private void HandleStatusMessageReceived(object sender, MessageEventArgs e)
        {
            MessageConsumer.OnMessageReceived -= HandleStatusMessageReceived;
            MessageConsumer.OnMessageReceived += HandleMessageReceived;

            var message = e.Message.Data as DaqifiOutMessage;

            PopulateDigitalChannels(message);
            PopulateAnalogInChannels(message);
            PopulateAnalogOutChannels(message);
            PopulateNetworkConfiguration(message);

            _timestampFrequency = message.TimestampFreq;
        }

        private void HandleMessageReceived(object sender, MessageEventArgs e)
        {
            var message = e.Message.Data as DaqifiOutMessage;

            if (!message.HasMsgTimeStamp)
            {
                AppLogger.Warning("Message did not contain a timestamp.  Will ignore message");
                return;
            }

            if (_previousTimestamp == null)
            {
                // TODO this starting time is inaccurate due to transmission delays.
                // The board only sends relative timestamps based on a timestamp clock frequency
                _previousTimestamp = DateTime.Now;
                _previousDeviceTimestamp = message.MsgTimeStamp;
            }

            // Get timestamp difference (i.e. number of clock cycles between messages)
            // Check for rollover scenario
            uint numberOfClockCyclesBetweenMessages;
            if (_previousDeviceTimestamp > message.MsgTimeStamp)
            {
                var numberOfCyclesToMax = uint.MaxValue - _previousDeviceTimestamp.Value;
                numberOfClockCyclesBetweenMessages = numberOfCyclesToMax + message.MsgTimeStamp;
            }
            else
            {
                numberOfClockCyclesBetweenMessages = message.MsgTimeStamp - _previousDeviceTimestamp.Value;
            }

            // Convert clock cycles to a time value
            var secondsBetweenMessages = numberOfClockCyclesBetweenMessages / (double)_timestampFrequency;

            var messageTimestamp = _previousTimestamp.Value.AddMilliseconds(secondsBetweenMessages * 1000.0);

            //Update digital channel information
            var digitalCount = 0;
            var analogCount = 0;
            foreach (var channel in DataChannels)
            {
                if (channel.Direction != ChannelDirection.Input) continue;

                if (channel.Type == ChannelType.Digital)
                {
                    if (channel.IsActive)
                    {
                        var bit = (message.DigitalData.ElementAt(0) & (1 << digitalCount)) != 0;
                        channel.ActiveSample = new DataSample(this, channel, messageTimestamp, Convert.ToInt32(bit));
                    }
                    digitalCount++;
                }
                else if (channel.Type == ChannelType.Analog && channel.IsActive)
                {
                    if (analogCount > message.AnalogInDataList.Count - 1)
                    {
                        AppLogger.Error("Trying to access at least one more analog channel than we actually recieved.  This might happen if recently added an analog channel but not yet receiving data from it yet.");
                        break;
                    }

                    channel.ActiveSample = new DataSample(this, channel, messageTimestamp, ScaleAnalogSample(channel as AnalogChannel, message.AnalogInDataList.ElementAt(analogCount)));
                    analogCount++;
                }
            }

            // Updates the previous timestamps
            _previousDeviceTimestamp = message.MsgTimeStamp;
            _previousTimestamp = messageTimestamp;
        }
        #endregion

        #region Streaming Methods
        public void InitializeStreaming()
        {
            MessageProducer.SendAsync(ScpiMessagePoducer.StartStreaming(StreamingFrequency));
            IsStreaming = true;
        }

        public void StopStreaming()
        {
            IsStreaming = false;
            MessageProducer.SendAsync(ScpiMessagePoducer.StopStreaming);
            _previousTimestamp = null;
        }

        protected void TurnOffEcho()
        {
            MessageProducer.SendAsync(ScpiMessagePoducer.Echo(-1));
        }
        #endregion

        #region Channel Methods
        public void AddChannel(IChannel newChannel)
        {
            switch (newChannel.Type)
            {
                case ChannelType.Analog:
                    var activeAnalogChannels = GetActiveChannels(ChannelType.Analog);
                    var channelSetByte = 0;

                    //Get Exsiting Channel Set Byte
                    foreach (var activeChannel in activeAnalogChannels)
                    {
                        channelSetByte = channelSetByte | (1 << activeChannel.Index);
                    }

                    //Add Channel Bit to the Channel Set Byte
                    channelSetByte = channelSetByte | (1 << newChannel.Index);

                    //Convert to a string
                    var channelSetString = Convert.ToString(channelSetByte);

                    //Send the command to add the channel
                    MessageProducer.SendAsync(ScpiMessagePoducer.ConfigureAdcChannels(channelSetString));
                    break;
                case ChannelType.Digital:
                    MessageProducer.SendAsync(ScpiMessagePoducer.EnableDioPorts());
                    break;
            }

            newChannel.IsActive = true;
        }

        public void RemoveChannel(IChannel channelToRemove)
        {
            switch (channelToRemove.Type)
            {
                case ChannelType.Analog:
                    var activeAnalogChannels = GetActiveChannels(ChannelType.Analog);
                    var channelSetByte = 0;

                    //Get Exsiting Channel Set Byte
                    foreach (IChannel activeChannel in activeAnalogChannels)
                    {
                        channelSetByte = channelSetByte | (1 << activeChannel.Index);
                    }

                    //Add Channel Bit to the Channel Set Byte
                    channelSetByte = channelSetByte | (1 >> channelToRemove.Index);

                    //Convert to a string
                    var channelSetString = Convert.ToString(channelSetByte);

                    //Send the command to add the channel
                    MessageProducer.SendAsync(ScpiMessagePoducer.ConfigureAdcChannels(channelSetString));
                    break;
            }
        }

        private IEnumerable<IChannel> GetActiveChannels(ChannelType channelType)
        {
            switch (channelType)
            {
                case ChannelType.Analog:
                    return DataChannels.Where(channel => channel.Type == ChannelType.Analog && channel.IsActive).ToList();
                case ChannelType.Digital:
                    return DataChannels.Where(channel => channel.Type == ChannelType.Digital && channel.IsActive).ToList();
                default:
                    throw new NotImplementedException();
            }
        }

        public void SetChannelOutputValue(IChannel channel, double value)
        {
            switch (channel.Type)
            {
                case ChannelType.Analog:
                    MessageProducer.SendAsync(ScpiMessagePoducer.SetVoltageLevel(channel.Index, value));
                    break;
                case ChannelType.Digital:
                    MessageProducer.SendAsync(ScpiMessagePoducer.SetDioPortState(channel.Index, value));
                    break;
            }
        }

        public void SetChannelDirection(IChannel channel, ChannelDirection direction)
        {
            switch (direction)
            {
                case ChannelDirection.Input:
                    MessageProducer.SendAsync(ScpiMessagePoducer.SetDioPortDirection(channel.Index, 0));
                    break;
                case ChannelDirection.Output:
                    MessageProducer.SendAsync(ScpiMessagePoducer.SetDioPortDirection(channel.Index, 1));
                    break;
            }
        }

        public void SetAdcMode(IChannel channel, AdcMode mode)
        {
            switch (mode)
            {
                case AdcMode.Differential:
                    MessageProducer.SendAsync(ScpiMessagePoducer.ConfigureAdcMode(channel.Index, 0));
                    break;
                case AdcMode.SingleEnded:
                    MessageProducer.SendAsync(ScpiMessagePoducer.ConfigureAdcMode(channel.Index, 1));
                    break;
            }
        }

        public void SetAdcRange(int range)
        {
            switch (range)
            {
                case 5:
                    MessageProducer.SendAsync(ScpiMessagePoducer.ConfigureAdcRange(0));
                    AdcRange = 0;
                    break;
                case 10:
                    MessageProducer.SendAsync(ScpiMessagePoducer.ConfigureAdcRange(1));
                    AdcRange = 1;
                    break;
            }
        }

        private void PopulateNetworkConfiguration(IDaqifiOutMessage message)
        {
            if (message.HasSsid)
            {
                NetworkConfiguration.Ssid = message.Ssid;
            }

            if (message.HasWifiSecurityMode)
            {
                NetworkConfiguration.SecurityType = (WifiSecurityType)message.WifiSecurityMode;
            }
        }

        private void PopulateAnalogInChannels(IDaqifiOutMessage message)
        {
            if (!message.HasAnalogInPortNum) return;

            var analogInPortRanges = message.AnalogInPortRangeList;
            var analogInCalibrationBValues = message.AnalogInCalBList;
            var analogInCalibrationMValues = message.AnalogInCalMList;
            var analogInInternalScaleMValues = message.AnalogInIntScaleMList;
            var analogInResolution = message.AnalogInRes;

            if (analogInCalibrationBValues.Count != analogInCalibrationMValues.Count ||
                analogInCalibrationBValues.Count != message.AnalogInPortNum)
            {
                // TODO handle mismatch.  Probably not add any channels and warn the user something went wrong.
            }

            for (var i = 0; i < message.AnalogInPortNum; i++)
            {
                DataChannels.Add(new AnalogChannel(this, "AI" + i, i, ChannelDirection.Input, false, analogInCalibrationBValues[i], analogInCalibrationMValues[i], analogInInternalScaleMValues[i], analogInPortRanges[i], analogInResolution));
            }
        }

        private void PopulateDigitalChannels(IDaqifiOutMessage message)
        {
            if (!message.HasDigitalPortNum) return;
            for (var i = 0; i < message.DigitalPortNum; i++)
            {
                DataChannels.Add(new DigitalChannel(this, "DIO" + i, i, ChannelDirection.Input, true));
            }
        }

        private void PopulateAnalogOutChannels(DaqifiOutMessage message)
        {
            if (!message.HasAnalogOutPortNum) return;

            // TODO handle HasAnalogOutPortNum.  Firmware doesn't yet have this field
        }
        #endregion

        public void InitializeDeviceState()
        {
            MessageConsumer.OnMessageReceived += HandleStatusMessageReceived;
            MessageProducer.SendAsync(ScpiMessagePoducer.SystemInfo);
        }

        private double ScaleAnalogSample(AnalogChannel channel, double analogValue)
        {
            return (analogValue / channel.Resolution) * channel.PortRange * channel.CalibrationMValue *
                   channel.InternalScaleMValue + channel.CalibrationBValue;
        }

        public void UpdateNetworkConfiguration()
        {
            if (IsStreaming) StopStreaming();
            MessageProducer.SendAsync(ScpiMessagePoducer.SetWifiMode(NetworkConfiguration.Mode));
            Thread.Sleep(100);
            MessageProducer.SendAsync(ScpiMessagePoducer.SetSsid(NetworkConfiguration.Ssid));
            Thread.Sleep(100);
            MessageProducer.SendAsync(ScpiMessagePoducer.SetSecurity(NetworkConfiguration.SecurityType));
            Thread.Sleep(100);
            MessageProducer.SendAsync(ScpiMessagePoducer.SetPassword(NetworkConfiguration.Password));
            Thread.Sleep(100);
            MessageProducer.SendAsync(ScpiMessagePoducer.ApplyLan());
            Thread.Sleep(100);
            MessageProducer.SendAsync(ScpiMessagePoducer.SaveLan());
            Thread.Sleep(100);
            Reboot();
        }

        public void Reboot()
        {
            MessageProducer.SendAsync(ScpiMessagePoducer.Reboot);
        }
    }
}