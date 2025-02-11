using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.DataModel.Channel;
using Daqifi.Desktop.DataModel.Network;
using Daqifi.Desktop.IO.Messages;
using Daqifi.Desktop.IO.Messages.Consumers;
using Daqifi.Desktop.IO.Messages.Producers;
using Microsoft.Extensions.ObjectPool;
using Daqifi.Desktop.IO.Messages.Decoders;
using Daqifi.Desktop.Loggers;
using Bugsnag.Payload;

namespace Daqifi.Desktop.Device
{
    public abstract class AbstractStreamingDevice : ObservableObject, IStreamingDevice
    {
        private const string _5Volt = "+/-5V";
        private const string _10Volt = "+/-10V";
        private const string Nq1PartNumber = "Nq1";
        private const string Nq2PartNumber = "Nq2";
        private const string Nq3PartNumber = "Nq3";
        private const double TickPeriod = 20E-9f;
        private static DateTime? _previousTimestamp;
        private string _adcRangeText;
        protected readonly double AdcResolution = 131072;
        protected double AdcRange = 1;
        private int _streamingFrequency = 1;
        private uint? _previousDeviceTimestamp;

        private Dictionary<string, DateTime> _previousTimestamps = new Dictionary<string, DateTime>();
        private Dictionary<string, uint?> _previousDeviceTimestamps = new Dictionary<string, uint?>();
        private ObjectPool<DataSample> _samplePool;
        private ObjectPool<DeviceMessage> _deviceMessagePool;

        #region Properties
        public AppLogger AppLogger = AppLogger.Instance;

        public int Id { get; set; }

        public string Name { get; set; }
        public string MacAddress { get; set; }

        public string DevicePartNumber { get; private set; } = string.Empty;

        public string DeviceSerialNo { get; set; } = string.Empty;

        public string DeviceVersion { get; set; }

        public string IpAddress { get; set; } = string.Empty;
        public int StreamingFrequency
        {
            get => _streamingFrequency;
            set
            {
                if (value < 1) { return; }
                _streamingFrequency = value;
                NotifyPropertyChanged("StreamingFrequency");
            }
        }

        public List<string> SecurityTypes { get; } = new List<string>();
        public List<string> AdcRanges { get; } = new List<string>();

        public NetworkConfiguration NetworkConfiguration { get; set; } = new NetworkConfiguration();

        public IMessageConsumer MessageConsumer { get; set; }
        public IMessageProducer MessageProducer { get; set; }
        public List<IChannel> DataChannels { get; set; } = new List<IChannel>();

        public string AdcRangeText
        {
            get => _adcRangeText;
            set
            {
                if (value == _adcRangeText)
                {
                    return;
                }

                switch (value)
                {
                    case _5Volt:
                        SetAdcRange(5);
                        break;
                    case _10Volt:
                        SetAdcRange(10);
                        break;
                    default:
                        return;
                }
                _adcRangeText = value;
                NotifyPropertyChanged("AdcRange");
            }
        }

        public bool IsStreaming { get; set; }
        public bool IsFirmwareOutdated { get; set; }
        #endregion

        #region Abstract Methods
        public abstract bool Connect();

        public abstract bool Disconnect();

        public abstract bool Write(string command);
        #endregion

        #region Message Handlers
        private void HandleStatusMessageReceived(object sender, MessageEventArgs e)
        {
            var message = e.Message.Data as DaqifiOutMessage;
            if (message == null || !IsValidStatusMessage(message))
            {
                MessageProducer.Send(ScpiMessagePoducer.SystemInfo);
                return;
            }

            // Change the message handler
            MessageConsumer.OnMessageReceived -= HandleStatusMessageReceived;
            MessageConsumer.OnMessageReceived += HandleMessageReceived;

            HydrateDeviceMetadata(message);
            PopulateDigitalChannels(message);
            PopulateAnalogInChannels(message);
            PopulateAnalogOutChannels(message);
        }

        private bool IsValidStatusMessage(DaqifiOutMessage message)
        {
            return (message.DigitalPortNum != 0 || message.AnalogInPortNum != 0 || message.AnalogOutPortNum != 0);
        }

        private void HandleMessageReceived(object sender, MessageEventArgs e)
        {
            if (!IsStreaming)
            {
                return;
            }

            if (!(e.Message.Data is DaqifiOutMessage message))
            {
                AppLogger.Warning("Issue decoding protobuf message");
                return;
            }

            if (message.MsgTimeStamp == 0)
            {
                AppLogger.Warning("Protobuf message did not contain a timestamp. Will ignore message");
                return;
            }

            var deviceId = message.DeviceSn.ToString();

            if (!_previousTimestamps.ContainsKey(deviceId))
            {
                _previousTimestamps[deviceId] = DateTime.Now;
                _previousDeviceTimestamps[deviceId] = message.MsgTimeStamp;
            }

            DateTime previousTimestamp = _previousTimestamps[deviceId];
            uint previousDeviceTimestamp = _previousDeviceTimestamps[deviceId].GetValueOrDefault();

            uint numberOfClockCyclesBetweenMessages;
            var rollover = previousDeviceTimestamp > message.MsgTimeStamp;
            if (rollover)
            {
                var numberOfCyclesToMax = uint.MaxValue - previousDeviceTimestamp;
                numberOfClockCyclesBetweenMessages = numberOfCyclesToMax + message.MsgTimeStamp;
            }
            else
            {
                numberOfClockCyclesBetweenMessages = message.MsgTimeStamp - previousDeviceTimestamp;
            }

            var secondsBetweenMessages = numberOfClockCyclesBetweenMessages * TickPeriod;

            if (rollover && secondsBetweenMessages > 10)
            {
                numberOfClockCyclesBetweenMessages = previousDeviceTimestamp - message.MsgTimeStamp;
                secondsBetweenMessages = numberOfClockCyclesBetweenMessages * TickPeriod * -1;
            }

            var messageTimestamp = previousTimestamp.AddSeconds(secondsBetweenMessages);

            var digitalCount = 0;
            var analogCount = 0;

            var digitalData1 = new byte();
            var digitalData2 = new byte();
            var hasDigitalData = message.DigitalData;

            if (hasDigitalData.Length > 0)
            {
                digitalData1 = hasDigitalData.ElementAtOrDefault(0);
                digitalData2 = hasDigitalData.ElementAtOrDefault(1);
            }

            // Loop through channels for this device
            foreach (var channel in DataChannels.Where(c => c.IsActive && c.Direction == ChannelDirection.Input))
            {
                try
                {
                    if (channel.Type == ChannelType.Digital && hasDigitalData.Length > 0)
                    {
                        bool bit;
                        if (digitalCount < 8)
                        {
                            bit = (digitalData1 & (1 << digitalCount)) != 0;
                        }
                        else
                        {
                            bit = (digitalData2 & (1 << digitalCount % 8)) != 0;
                        }

                        // Assign the sample for the digital channel
                        channel.ActiveSample = new DataSample(this, channel, messageTimestamp, Convert.ToInt32(bit));
                        digitalCount++;
                    }
                    else if (channel.Type == ChannelType.Analog)
                    {
                        if (analogCount >= message.AnalogInData.Count)
                        {
                            AppLogger.Error("Trying to access more analog channels than received data.");
                            break;
                        }

                        // Process the analog sample
                        var sample = new DataSample(this, channel, messageTimestamp, ScaleAnalogSample(channel as AnalogChannel, message.AnalogInData.ElementAt(analogCount)));
                        channel.ActiveSample = sample;
                        analogCount++;
                    }
                }
                catch (System.Exception ex)
                {
                    AppLogger.Error($"Error processing channel data: {ex.Message}");
                }
            }

            var deviceMessage = new DeviceMessage()
            {
                DeviceName = Name,
                AnalogChannelCount = analogCount,
                DeviceSerialNo = message.DeviceSn.ToString(),
                DeviceVersion = message.DeviceFwRev.ToString(),
                DigitalChannelCount = digitalCount,
                TimestampTicks = messageTimestamp.Ticks,
                AppTicks = DateTime.Now.Ticks,
                DeviceStatus = (int)message.DeviceStatus,
                BatteryStatus = (int)message.BattStatus,
                PowerStatus = (int)message.PwrStatus,
                TempStatus = (int)message.TempStatus,
                TargetFrequency = (int)message.TimestampFreq,
                Rollover = rollover,
            };

            Logger.LoggingManager.Instance.HandleDeviceMessage(this, deviceMessage);

            _previousTimestamps[deviceId] = messageTimestamp;
            _previousDeviceTimestamps[deviceId] = message.MsgTimeStamp;
        }


        #endregion

        #region Streaming Methods
        public void InitializeStreaming()
        {
            _previousTimestamp = null;
            MessageProducer.Send(ScpiMessagePoducer.StartStreaming(StreamingFrequency));
            IsStreaming = true;
            var objectPoolProvider = new DefaultObjectPoolProvider(); // Initialize pools with default policy
            _samplePool = objectPoolProvider.Create<DataSample>();
            _deviceMessagePool = objectPoolProvider.Create<DeviceMessage>();
        }

        public void StopStreaming()
        {
            IsStreaming = false;
            MessageProducer.Send(ScpiMessagePoducer.StopStreaming);
            _previousTimestamp = null;

            foreach (var channel in DataChannels)
            {
                if (channel.ActiveSample != null)
                {
                    channel.ActiveSample = null;
                }
            }
        }

        protected void TurnOffEcho()
        {
            MessageProducer.Send(ScpiMessagePoducer.Echo(-1));
        }

        protected void TurnDeviceOn()
        {
            MessageProducer.Send(ScpiMessagePoducer.DeviceOn);
        }

        protected void SetProtobufMessageFormat()
        {
            MessageProducer.Send(ScpiMessagePoducer.SetProtobufStreamFormat);
        }
        #endregion

        #region Channel Methods
        public void AddChannel(IChannel channelToAdd)
        {
            var channel = DataChannels.FirstOrDefault(c => Equals(c, channelToAdd));
            if (channel == null)
            {
                AppLogger.Error($"There was a problem adding channel: {channelToAdd.Name}.  " +
                               $"Trying to add a channel that does not belong to the device: {Name}");
                return;
            }

            switch (channel.Type)
            {
                case ChannelType.Analog:
                    var activeAnalogChannels = GetActiveAnalogChannels();
                    var channelSetByte = 0;

                    // Get Exsiting Channel Set Byte
                    foreach (var activeChannel in activeAnalogChannels)
                    {
                        channelSetByte = channelSetByte | (1 << activeChannel.Index);
                    }

                    // Add Channel Bit to the Channel Set Byte
                    channelSetByte = channelSetByte | (1 << channel.Index);

                    // Convert to a string
                    var channelSetString = Convert.ToString(channelSetByte);

                    // Send the command to add the channel
                    MessageProducer.Send(ScpiMessagePoducer.EnableAdcChannels(channelSetString));
                    break;
                case ChannelType.Digital:
                    MessageProducer.Send(ScpiMessagePoducer.EnableDioPorts());
                    break;
            }

            channel.IsActive = true;
        }

        public void RemoveChannel(IChannel channelToRemove)
        {
            switch (channelToRemove.Type)
            {
                case ChannelType.Analog:
                    var activeAnalogChannels = GetActiveAnalogChannels();
                    var channelSetByte = 0;

                    //Get Exsiting Channel Set Byte
                    foreach (var activeChannel in activeAnalogChannels)
                    {
                        channelSetByte = channelSetByte | (1 << activeChannel.Index);
                    }

                    //Add Channel Bit to the Channel Set Byte
                    channelSetByte = channelSetByte | (1 >> channelToRemove.Index);

                    //Convert to a string
                    var channelSetString = Convert.ToString(channelSetByte);

                    //Send the command to add the channel
                    MessageProducer.Send(ScpiMessagePoducer.EnableAdcChannels(channelSetString));
                    break;
            }

            var channel = DataChannels.FirstOrDefault(c => Equals(c, channelToRemove));
            if (channel == null)
            {
                AppLogger.Error($"There was a problem removing channel: {channelToRemove.Name}");
            }
            else
            {
                channel.IsActive = false;
            }
        }

        private IEnumerable<IChannel> GetActiveAnalogChannels()
        {
            return DataChannels.Where(channel => channel.Type == ChannelType.Analog && channel.IsActive).ToList();
        }

        public void SetChannelOutputValue(IChannel channel, double value)
        {
            switch (channel.Type)
            {
                case ChannelType.Analog:
                    MessageProducer.Send(ScpiMessagePoducer.SetVoltageLevel(channel.Index, value));
                    break;
                case ChannelType.Digital:
                    MessageProducer.Send(ScpiMessagePoducer.SetDioPortState(channel.Index, value));
                    break;
            }
        }

        public void SetChannelDirection(IChannel channel, ChannelDirection direction)
        {
            switch (direction)
            {
                case ChannelDirection.Input:
                    MessageProducer.Send(ScpiMessagePoducer.SetDioPortDirection(channel.Index, 0));
                    break;
                case ChannelDirection.Output:
                    MessageProducer.Send(ScpiMessagePoducer.SetDioPortDirection(channel.Index, 1));
                    break;
            }
        }

        public void SetAdcMode(IChannel channel, AdcMode mode)
        {
            switch (mode)
            {
                case AdcMode.Differential:
                    MessageProducer.Send(ScpiMessagePoducer.ConfigureAdcMode(channel.Index, 0));
                    break;
                case AdcMode.SingleEnded:
                    MessageProducer.Send(ScpiMessagePoducer.ConfigureAdcMode(channel.Index, 1));
                    break;
            }
        }

        public void SetAdcRange(int range)
        {
            switch (range)
            {
                case 5:
                    MessageProducer.Send(ScpiMessagePoducer.ConfigureAdcRange(0));
                    AdcRange = 0;
                    break;
                case 10:
                    MessageProducer.Send(ScpiMessagePoducer.ConfigureAdcRange(1));
                    AdcRange = 1;
                    break;
            }
        }

        private void PopulateAnalogInChannels(DaqifiOutMessage message)
        {
            if (message.AnalogInPortNum == 0) { return; }

            if (!string.IsNullOrWhiteSpace(DevicePartNumber))
            {
                AdcRanges.Clear();
                if (DevicePartNumber == Nq1PartNumber)
                {
                    AdcRanges.Add(_5Volt);
                }
                else if (DevicePartNumber == Nq2PartNumber || DevicePartNumber == Nq3PartNumber)
                {
                    AdcRanges.Add(_5Volt);
                    AdcRanges.Add(_10Volt);
                }
            }

            var analogInPortRanges = message.AnalogInPortRange;
            var analogInCalibrationBValues = message.AnalogInCalB;
            var analogInCalibrationMValues = message.AnalogInCalM;
            var analogInInternalScaleMValues = message.AnalogInIntScaleM;
            var analogInResolution = message.AnalogInRes;

            if (analogInCalibrationBValues.Count != analogInCalibrationMValues.Count ||
                analogInCalibrationBValues.Count != message.AnalogInPortNum)
            {
                // TODO handle mismatch.  Probably not add any channels and warn the user something went wrong.
            }

            Func<IList<float>, int, float, float> getWithDefault = (IList<float> list, int idx, float def) =>
            {
                if (list.Count > idx)
                {
                    return list[idx];
                }

                return def;
            };

            for (var i = 0; i < message.AnalogInPortNum; i++)
            {
                DataChannels.Add(new AnalogChannel(this, "AI" + i, i, ChannelDirection.Input, false,
                    getWithDefault(analogInCalibrationBValues, i, 0.0f),
                    getWithDefault(analogInCalibrationMValues, i, 1.0f),
                    getWithDefault(analogInInternalScaleMValues, i, 1.0f),
                    getWithDefault(analogInPortRanges, i, 1.0f),
                    analogInResolution));
            }
        }

        private void HydrateDeviceMetadata(DaqifiOutMessage message)
        {
            if (!string.IsNullOrWhiteSpace(message.Ssid))
            {
                NetworkConfiguration.Ssid = message.Ssid;
            }

            if (message.WifiSecurityMode > 0)
            {
                NetworkConfiguration.SecurityType = (WifiSecurityType)message.WifiSecurityMode;
            }

            if (message.WifiInfMode > 0)
            {
                NetworkConfiguration.Mode = (WifiMode)message.WifiInfMode;
            }
            if (!string.IsNullOrWhiteSpace(message.DevicePn))
            {
                DevicePartNumber = message.DevicePn;
            }
            if (message.DeviceSn != 0)
            {
                DeviceSerialNo = message.DeviceSn.ToString();
            }
            if (!string.IsNullOrWhiteSpace(message.DeviceFwRev))
            {
                DeviceVersion = message.DeviceFwRev.ToString();
            }
            if (message.IpAddr != null && message.IpAddr.Length > 0)
            {
                IpAddress = string.Join(",", message.IpAddr);
            }
            if (message.MacAddr.Length > 0)
            {
                MacAddress = ProtobufDecoder.GetMacAddressString(message);
            }

            if (message.AnalogInPortRange.Count > 0 && (int)message.AnalogInPortRange[0] == 5)
            {
                _adcRangeText = _5Volt;
            }
        }

        private void PopulateDigitalChannels(DaqifiOutMessage message)
        {

            if (message.DigitalPortNum == 0) { return; }

            for (var i = 0; i < message.DigitalPortNum; i++)
            {
                DataChannels.Add(new DigitalChannel(this, "DIO" + i, i, ChannelDirection.Input, true));
            }
        }

        private void PopulateAnalogOutChannels(DaqifiOutMessage message)
        {
            if (message.AnalogOutPortNum == 0) { return; }

            // TODO handle HasAnalogOutPortNum.  Firmware doesn't yet have this field
        }
        #endregion

        public void InitializeDeviceState()
        {
            MessageConsumer.OnMessageReceived += HandleStatusMessageReceived;
            MessageProducer.Send(ScpiMessagePoducer.SystemInfo);
        }

        private static double ScaleAnalogSample(AnalogChannel channel, double analogValue)
        {
            return (analogValue / channel.Resolution) * channel.PortRange * channel.CalibrationMValue *
                   channel.InternalScaleMValue + channel.CalibrationBValue;
        }

        public void UpdateNetworkConfiguration()
        {
            if (IsStreaming) { StopStreaming(); }
            MessageProducer.Send(ScpiMessagePoducer.SetWifiMode(NetworkConfiguration.Mode));
            MessageProducer.Send(ScpiMessagePoducer.SetSsid(NetworkConfiguration.Ssid));
            MessageProducer.Send(ScpiMessagePoducer.SetSecurity(NetworkConfiguration.SecurityType));
            MessageProducer.Send(ScpiMessagePoducer.SetPassword(NetworkConfiguration.Password));
            MessageProducer.Send(ScpiMessagePoducer.ApplyLan());
            MessageProducer.Send(ScpiMessagePoducer.SaveLan());
            ConnectionManager.Instance.Reboot(this);
        }

        public void Reboot()
        {
            MessageProducer.Send(ScpiMessagePoducer.Reboot);
            MessageProducer.StopSafely();
            MessageConsumer.Stop();
        }
    }
}