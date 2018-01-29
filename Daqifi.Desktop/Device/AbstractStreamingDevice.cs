using Daqifi.Desktop.Channel;
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

        public abstract void InitializeStreaming();

        public abstract void StopStreaming();

        public abstract void InitializeDeviceState();

        public abstract void SetAdcMode(IChannel channel, AdcMode mode);

        public abstract void SetAdcRange(int range);

        public abstract void AddChannel(IChannel channel);

        public abstract void RemoveChannel(IChannel channel);

        public abstract void UpdateFirmware(byte[] data);

        public abstract void SetChannelOutputValue(IChannel channel, double value);

        public abstract void SetChannelDirection(IChannel channel, ChannelDirection direction);

        #endregion

        protected void HandleStatusMessageReceived(object sender, MessageEventArgs e)
        {
            MessageConsumer.OnMessageReceived -= HandleStatusMessageReceived;
            MessageConsumer.OnMessageReceived += HandleMessageReceived;

            var message = e.Message.Data as DaqifiOutMessage;

            AddDigitalChannels(message);
            AddAnalogInChannels(message);
            AddAnalogOutChannels(message);
            AddNetworkConfiguration(message);

            _timestampFrequency = message.TimestampFreq;
        }

        private void AddNetworkConfiguration(DaqifiOutMessage message)
        {
            if (message.HasSsid)
            {
                NetworkConfiguration.Ssid = message.Ssid;
            }
            if(message.HasWifiSecurityMode)
            {
                NetworkConfiguration.SecurityType = (WifiSecurityType)message.WifiSecurityMode;
            }
        }

        private void AddAnalogInChannels(DaqifiOutMessage message)
        {
            if (message.HasAnalogInPortNum)
            {
                for (var i = 0; i < message.AnalogInPortNum; i++)
                    DataChannels.Add(new AnalogChannel(this, "AI" + i, i, ChannelDirection.Input, false));
            }
        }

        private void AddDigitalChannels(DaqifiOutMessage message)
        {
            if (message.HasDigitalPortNum)
            {
                for (var i = 0; i < message.DigitalPortNum; i++)
                    DataChannels.Add(new DigitalChannel(this, "DIO" + i, i, ChannelDirection.Input, true));
            }
        }

        private void AddAnalogOutChannels(DaqifiOutMessage message)
        {
            // TODO handle HasAnalogOutPortNum.  Firmware doesn't yet have this field
        }

        protected void HandleMessageReceived(object sender, MessageEventArgs e)
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
            var secondsBetweenMessages = numberOfClockCyclesBetweenMessages / (double) _timestampFrequency;

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

                    channel.ActiveSample = new DataSample(this, channel, messageTimestamp, ScaleAnalogSample(message.AnalogInDataList.ElementAt(analogCount)));
                    analogCount++;
                }
            }

            // Updates the previous timestamps
            _previousDeviceTimestamp = message.MsgTimeStamp;
            _previousTimestamp = messageTimestamp;
        }

        private double ScaleAnalogSample(double sampleValue)
        {
            return sampleValue * (AdcRange * 10.0 + 10.0) / AdcResolution;
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