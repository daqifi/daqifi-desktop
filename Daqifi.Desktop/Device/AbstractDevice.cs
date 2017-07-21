using System;
using System.Collections.Generic;
using System.Linq;
using Daqifi.Desktop.Message;
using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Loggers;

namespace Daqifi.Desktop.Device
{
    public abstract class AbstractDevice : ObservableObject, IDevice
    {
        #region Private Data

        protected static DateTime? _firstTime;
        protected static uint _firstMessageSequence;
        private string _AdcRangeText;
        protected readonly double _ADCResolution = 131072;
        protected double _ADCRange = 1;
        private int _streamingFrequency = 1;
        #endregion

        #region Properties
        public AppLogger AppLogger = AppLogger.Instance;

        public int Id { get; set; }

        public string Name { get; set; }

        public int StreamingFrequency
        {
            get { return _streamingFrequency; }
            set
            {
                if (value < 1) return;
                _streamingFrequency = value;
                NotifyPropertyChanged("StreamingFrequency");
            }
        }

        public List<string> SecurityTypes { get; } = new List<string> { "None (Open Network)", "WEP-40", "WEP-104", "WPA-PSK Phrase", "WPA-PSK Key" };
        public List<string> ADCRanges { get; } = new List<string> { "+/-5V", "+/-10V" };

        public NetworkConfiguration NetworkConfiguration { get; set; } = new NetworkConfiguration();

        public IMessageConsumer MessageConsumer { get; set; }
        public IMessageProducer MessageProducer { get; set; }
        public List<IChannel> DataChannels { get; set; }

        public string AdcRangeText
        {
            get { return _AdcRangeText; }
            set 
            {
                if(value == ADCRanges[0])
                {
                    SetAdcRange(5);
                }
                else if (value == ADCRanges[1])
                {
                    SetAdcRange(10);
                }
                else
                {
                    return;
                }
                _AdcRangeText = value;
                NotifyPropertyChanged("ADCRange");
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

        public abstract void UpdateNetworkConfiguration();

        public abstract void UpdateFirmware(byte[] data);

        public abstract void SetChannelOutputValue(IChannel channel, double value);

        public abstract void SetChannelDirection(IChannel channel, ChannelDirection direction);

        public abstract void Reboot();
        #endregion

        protected void StatusMessageReceived(object sender, MessageEventArgs e)
        {
            MessageConsumer.OnMessageReceived -= StatusMessageReceived;

            var message = e.Message.Data as WiFiDAQOutMessage;
            foreach (var key in message.DevicePn.ToLower().Split('-'))
            {
                if (key.StartsWith("ai"))
                {
                    int analogInputCount = int.Parse(key.Substring(2));
                    for (int i = 0; i < analogInputCount; i++) DataChannels.Add(new AnalogChannel(this, "AI" + i, i, ChannelDirection.Input, false));
                }

                if (key.StartsWith("ao"))
                {
                    int analogOutputCount = int.Parse(key.Substring(2));
                    for (int i = 0; i < analogOutputCount; i++) DataChannels.Add(new AnalogChannel(this, "AO" + i, i, ChannelDirection.Output, false));
                }

                if (key.StartsWith("dio"))
                {
                    int digitalCount = int.Parse(key.Substring(3));
                    for (int i = 0; i < digitalCount; i++) DataChannels.Add(new DigitalChannel(this, "DIO" + i, i, ChannelDirection.Input, true));
                }
            }

            MessageConsumer.OnMessageReceived += MessageReceived;
        }

        protected void MessageReceived(object sender, MessageEventArgs e)
        {
            var message = e.Message.Data as WiFiDAQOutMessage;

            if (_firstTime == null)
            {
                _firstTime = DateTime.Now;
                _firstMessageSequence = message.MsgSeq;
            }

            double relativeTimestamp = Convert.ToDouble(message.MsgSeq - _firstMessageSequence) / StreamingFrequency;
            var timestamp = _firstTime.Value.AddMilliseconds(relativeTimestamp * 1000);

            //Update digital channel information
            int digitalCount = 0;
            int analogCount = 0;
            foreach (var channel in DataChannels)
            {
                if (channel.Direction != ChannelDirection.Input) continue;

                if (channel.Type == ChannelType.Digital)
                {
                    if (channel.IsActive)
                    {
                        bool bit = (message.DigitalData.ElementAt(0) & (1 << digitalCount)) != 0;
                        channel.ActiveSample = new DataSample(this, channel, timestamp, Convert.ToInt32(bit));
                    }
                    digitalCount++;
                }
                else if (channel.Type == ChannelType.Analog && channel.IsActive)
                {
                    if (analogCount > message.AnalogInDataIList.Count - 1)
                    {
                        AppLogger.Error("Trying to access at least one more analog channel than we actually recieved.  This might happen if recently added an analog channel but not yet receiving data from it yet.");
                        break;
                    }

                    channel.ActiveSample = new DataSample(this, channel, timestamp, ScaleSample(message.AnalogInDataIList.ElementAt(analogCount)));
                    analogCount++;
                }
            }
        }

        private double ScaleSample(double sampleValue)
        {
            return sampleValue * (_ADCRange * 10.0 + 10.0) / _ADCResolution;
        }
    }
}
