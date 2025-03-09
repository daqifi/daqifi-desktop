using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.DataModel.Channel;
using Daqifi.Desktop.DataModel.Network;
using Daqifi.Desktop.IO.Messages;
using Daqifi.Desktop.IO.Messages.Consumers;
using Daqifi.Desktop.IO.Messages.Producers;
using Microsoft.Extensions.ObjectPool;
using Daqifi.Desktop.IO.Messages.Decoders;
using Daqifi.Desktop.Models;
using System.Text.RegularExpressions;
using System.Globalization;
using System.IO;

namespace Daqifi.Desktop.Device
{
    public abstract class AbstractStreamingDevice : ObservableObject, IStreamingDevice
    {
        private enum MessageHandlerType
        {
            Status,
            Streaming,
            SdCard
        }

        private MessageHandlerType _currentHandler;
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
        private DeviceMode _mode = DeviceMode.StreamToApp;
        private bool _isLoggingToSdCard;
        private List<SdCardFile> _sdCardFiles = new();

        #region Properties
        public AppLogger AppLogger = AppLogger.Instance;

        public DeviceMode Mode => _mode;
        public bool IsLoggingToSdCard => _isLoggingToSdCard;
        public IReadOnlyList<SdCardFile> SdCardFiles => _sdCardFiles.AsReadOnly();

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
        private void SetMessageHandler(MessageHandlerType handlerType)
        {
            // Remove all handlers first
            if (MessageConsumer != null)
            {
                MessageConsumer.OnMessageReceived -= HandleStatusMessageReceived;
                MessageConsumer.OnMessageReceived -= HandleMessageReceived;
                MessageConsumer.OnMessageReceived -= HandleSdCardMessageReceived;

                // Add the new handler
                switch (handlerType)
                {
                    case MessageHandlerType.Status:
                        MessageConsumer.OnMessageReceived += HandleStatusMessageReceived;
                        break;
                    case MessageHandlerType.Streaming:
                        MessageConsumer.OnMessageReceived += HandleMessageReceived;
                        break;
                    case MessageHandlerType.SdCard:
                        MessageConsumer.OnMessageReceived += HandleSdCardMessageReceived;
                        break;
                }

                _currentHandler = handlerType;
                AppLogger.Information($"Message handler set to: {handlerType}");
            }
        }

        private void HandleStatusMessageReceived(object sender, MessageEventArgs e)
        {
            var message = e.Message.Data as DaqifiOutMessage;
            if (message == null || !IsValidStatusMessage(message))
            {
                MessageProducer.Send(ScpiMessageProducer.SystemInfo);
                return;
            }

            // Change the message handler
            SetMessageHandler(MessageHandlerType.Streaming);

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

        private void HandleSdCardMessageReceived(object sender, MessageEventArgs e)
        {
            // The message will be a string containing file paths
            if (e.Message.Data is not string response)
            {
                AppLogger.Warning("Expected string response for SD card operation");
                return;
            }

            try
            {
                // Check if this is a file content response (contains JSON or __END_OF_FILE__ marker)
                if (response.Contains("__END_OF_FILE__") || response.Contains("\"timestamp\""))
                {
                    HandleFileContentResponse(response);
                }
                // Check if this is a file list response (contains multiple lines with .bin files)
                else if (response.Contains(".bin"))
                {
                    HandleFileListResponse(response);
                    // We're done with the text consumer, stop it
                    MessageConsumer.Stop();
                }
                else
                {
                    AppLogger.Warning($"Unexpected SD card response format: {response.Substring(0, Math.Min(100, response.Length))}");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Error processing SD card message");
            }
        }

        private void HandleFileListResponse(string response)
        {
            var files = response
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(path =>
                {
                    // Clean up the path and handle the malformed first line
                    var cleanPath = path.Trim();
                    if (cleanPath.StartsWith("efault.bin"))
                    {
                        cleanPath = "Daqifi/default.bin";
                    }
                    else if (!cleanPath.StartsWith("Daqifi/"))
                    {
                        cleanPath = "Daqifi/" + cleanPath;
                    }

                    // Remove the Daqifi/ prefix if present and get just the filename
                    var fileName = Path.GetFileName(cleanPath);
                    
                    // For log files, try to parse the date from the filename
                    var createdDate = TryParseLogFileDate(fileName) ?? DateTime.MinValue;
                    
                    return new SdCardFile
                    {
                        FileName = fileName,
                        CreatedDate = createdDate
                    };
                })
                .Where(file => !string.IsNullOrEmpty(file.FileName))
                .ToList();

            AppLogger.Information($"Found {files.Count} files on SD card");
            UpdateSdCardFiles(files);
        }

        private void HandleFileContentResponse(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                AppLogger.Warning("Received empty file content response");
                return;
            }

            // Remove the end of file marker if present
            var cleanContent = content;
            if (content.Contains("__END_OF_FILE__"))
            {
                cleanContent = content.Replace("__END_OF_FILE__", "").Trim();
            }

            AppLogger.Information($"Received file content of length: {cleanContent.Length}");
            
            // Raise an event to notify that file content is available
            var args = new FileDownloadEventArgs(cleanContent);
            OnFileDownloaded?.Invoke(this, args);
        }

        public event EventHandler<FileDownloadEventArgs> OnFileDownloaded;

        private DateTime? TryParseLogFileDate(string fileName)
        {
            // Try to parse date from log_YYYYMMDD_HHMMSS.bin format
            var match = Regex.Match(fileName, @"log_(\d{8})_(\d{6})\.bin");
            if (match.Success)
            {
                var dateStr = match.Groups[1].Value;
                var timeStr = match.Groups[2].Value;
                if (DateTime.TryParseExact(
                    dateStr + timeStr, 
                    "yyyyMMddHHmmss", 
                    CultureInfo.InvariantCulture, 
                    DateTimeStyles.None, 
                    out DateTime result))
                {
                    return result;
                }
            }
            return null;
        }
        #endregion

        #region Streaming Methods
        public void SwitchMode(DeviceMode newMode)
        {
            if (_mode == newMode) return;
            
            // Stop any current activity
            if (IsStreaming)
            {
                StopStreaming();
            }
            if (IsLoggingToSdCard)
            {
                StopSdCardLogging();
            }
            
            // Clean up old mode
            switch (_mode)
            {
                case DeviceMode.StreamToApp:
                    StopMessageConsumer();
                    break;
                case DeviceMode.LogToDevice:
                    // Ensure SD card logging is stopped
                    StopSdCardLogging();
                    break;
            }

            _mode = newMode;

            // Setup new mode
            switch (_mode)
            {
                case DeviceMode.StreamToApp:
                    PrepareLanInterface();
                    break;
                case DeviceMode.LogToDevice:
                    PrepareSdInterface();
                    break;
            }

            NotifyPropertyChanged(nameof(Mode));
        }

        public void StartSdCardLogging()
        {
            if (Mode != DeviceMode.LogToDevice)
            {
                throw new InvalidOperationException("Cannot start SD card logging while in StreamToApp mode");
            }

            try
            {
                MessageProducer.Send(ScpiMessageProducer.EnableSdCard);
                MessageProducer.Send(ScpiMessageProducer.SetSdLoggingFileName($"log_{DateTime.Now:yyyyMMdd_HHmmss}.bin"));
                MessageProducer.Send(ScpiMessageProducer.SetProtobufStreamFormat); // Set format for SD card logging
                
                // Enable any active channels
                foreach (var channel in DataChannels.Where(c => c.IsActive))
                {
                    if (channel.Type == ChannelType.Analog)
                    {
                        var channelSetByte = 1 << channel.Index;
                        MessageProducer.Send(ScpiMessageProducer.EnableAdcChannels(channelSetByte.ToString()));
                    }
                    else if (channel.Type == ChannelType.Digital)
                    {
                        MessageProducer.Send(ScpiMessageProducer.EnableDioPorts());
                    }
                }

                // Start the device logging at the configured frequency
                MessageProducer.Send(ScpiMessageProducer.StartStreaming(StreamingFrequency));
                
                _isLoggingToSdCard = true;
                IsStreaming = true; // We're streaming to SD card
                AppLogger.Information($"Enabled SD card logging for device {DeviceSerialNo}");
                NotifyPropertyChanged(nameof(IsLoggingToSdCard));
                NotifyPropertyChanged(nameof(IsStreaming));
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, $"Failed to enable SD card logging for device {DeviceSerialNo}");
                throw;
            }
        }

        public void StopSdCardLogging()
        {
            try
            {
                // Stop the device logging
                MessageProducer.Send(ScpiMessageProducer.StopStreaming);
                MessageProducer.Send(ScpiMessageProducer.DisableSdCard);
                
                _isLoggingToSdCard = false;
                IsStreaming = false;
                AppLogger.Information($"Disabled SD card logging for device {DeviceSerialNo}");
                NotifyPropertyChanged(nameof(IsLoggingToSdCard));
                NotifyPropertyChanged(nameof(IsStreaming));
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, $"Failed to disable SD card logging for device {DeviceSerialNo}");
                throw;
            }
        }

        public void RefreshSdCardFiles()
        {
            PrepareSdInterface();

            // Create a text message consumer for SD card operations
            MessageConsumer = new TextMessageConsumer(MessageConsumer.DataStream);
            SetMessageHandler(MessageHandlerType.SdCard);
            
            if (!MessageConsumer.Running)
            {
                MessageConsumer.Start();
            }

            MessageProducer.Send(ScpiMessageProducer.GetSdFileList);
        }

        public void UpdateSdCardFiles(List<SdCardFile> files)
        {
            _sdCardFiles = files ?? new List<SdCardFile>();
            NotifyPropertyChanged(nameof(SdCardFiles));
        }

        public void InitializeStreaming()
        {
            if (Mode != DeviceMode.StreamToApp)
            {
                throw new InvalidOperationException("Cannot initialize streaming while in LogToDevice mode");
            }

            _previousTimestamp = null;
            MessageProducer.Send(ScpiMessageProducer.StartStreaming(StreamingFrequency));
            IsStreaming = true;
            StartMessageConsumer();
            var objectPoolProvider = new DefaultObjectPoolProvider(); // Initialize pools with default policy
            _samplePool = objectPoolProvider.Create<DataSample>();
            _deviceMessagePool = objectPoolProvider.Create<DeviceMessage>();
        }

        public void StopStreaming()
        {
            IsStreaming = false;
            MessageProducer.Send(ScpiMessageProducer.StopStreaming);
            StopMessageConsumer();
            _previousTimestamp = null;

            foreach (var channel in DataChannels)
            {
                if (channel.ActiveSample != null)
                {
                    channel.ActiveSample = null;
                }
            }
        }

        protected void StartMessageConsumer()
        {
            if (Mode != DeviceMode.StreamToApp)
            {
                return; // Don't start consumer if not in streaming mode
            }
            
            if (MessageConsumer != null)
            {
                SetMessageHandler(MessageHandlerType.Streaming);
                if (!MessageConsumer.Running)
                {
                    MessageConsumer.Start();
                }
            }
        }

        protected void StopMessageConsumer()
        {
            if (MessageConsumer != null)
            {
                SetMessageHandler(MessageHandlerType.Status);
                if (MessageConsumer.Running)
                {
                    MessageConsumer.Stop();
                }
            }
        }

        protected void TurnOffEcho()
        {
            MessageProducer.Send(ScpiMessageProducer.Echo(-1));
        }

        protected void TurnDeviceOn()
        {
            MessageProducer.Send(ScpiMessageProducer.DeviceOn);
        }

        protected void SetProtobufMessageFormat()
        {
            MessageProducer.Send(ScpiMessageProducer.SetProtobufStreamFormat);
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
                    MessageProducer.Send(ScpiMessageProducer.EnableAdcChannels(channelSetString));
                    break;
                case ChannelType.Digital:
                    MessageProducer.Send(ScpiMessageProducer.EnableDioPorts());
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
                    MessageProducer.Send(ScpiMessageProducer.EnableAdcChannels(channelSetString));
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
                    MessageProducer.Send(ScpiMessageProducer.SetVoltageLevel(channel.Index, value));
                    break;
                case ChannelType.Digital:
                    MessageProducer.Send(ScpiMessageProducer.SetDioPortState(channel.Index, value));
                    break;
            }
        }

        public void SetChannelDirection(IChannel channel, ChannelDirection direction)
        {
            switch (direction)
            {
                case ChannelDirection.Input:
                    MessageProducer.Send(ScpiMessageProducer.SetDioPortDirection(channel.Index, 0));
                    break;
                case ChannelDirection.Output:
                    MessageProducer.Send(ScpiMessageProducer.SetDioPortDirection(channel.Index, 1));
                    break;
            }
        }

        public void SetAdcMode(IChannel channel, AdcMode mode)
        {
            switch (mode)
            {
                case AdcMode.Differential:
                    MessageProducer.Send(ScpiMessageProducer.ConfigureAdcMode(channel.Index, 0));
                    break;
                case AdcMode.SingleEnded:
                    MessageProducer.Send(ScpiMessageProducer.ConfigureAdcMode(channel.Index, 1));
                    break;
            }
        }

        public void SetAdcRange(int range)
        {
            switch (range)
            {
                case 5:
                    MessageProducer.Send(ScpiMessageProducer.ConfigureAdcRange(0));
                    AdcRange = 0;
                    break;
                case 10:
                    MessageProducer.Send(ScpiMessageProducer.ConfigureAdcRange(1));
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
            SetMessageHandler(MessageHandlerType.Status);
            MessageProducer.Send(ScpiMessageProducer.SystemInfo);
        }

        private static double ScaleAnalogSample(AnalogChannel channel, double analogValue)
        {
            return (analogValue / channel.Resolution) * channel.PortRange * channel.CalibrationMValue *
                   channel.InternalScaleMValue + channel.CalibrationBValue;
        }

        public void UpdateNetworkConfiguration()
        {
            if (IsStreaming) { StopStreaming(); }
            MessageProducer.Send(ScpiMessageProducer.SetWifiMode(NetworkConfiguration.Mode));
            MessageProducer.Send(ScpiMessageProducer.SetSsid(NetworkConfiguration.Ssid));
            MessageProducer.Send(ScpiMessageProducer.SetSecurity(NetworkConfiguration.SecurityType));
            MessageProducer.Send(ScpiMessageProducer.SetPassword(NetworkConfiguration.Password));
            MessageProducer.Send(ScpiMessageProducer.ApplyLan);
            MessageProducer.Send(ScpiMessageProducer.SaveLan);
        }

        public void Reboot()
        {
            MessageProducer.Send(ScpiMessageProducer.Reboot);
            MessageProducer.StopSafely();
            MessageConsumer.Stop();
        }

        // SD and LAN can't both be enabled due to hardware limitations
        private void PrepareSdInterface()
        {
            MessageProducer.Send(ScpiMessageProducer.DisableLan);
            MessageProducer.Send(ScpiMessageProducer.EnableSdCard);
        }
        
        // SD and LAN can't both be enabled due to hardware limitations
        private void PrepareLanInterface()
        {
            MessageProducer.Send(ScpiMessageProducer.DisableSdCard);
            MessageProducer.Send(ScpiMessageProducer.EnableLan);
        }
    }
}