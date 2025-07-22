using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.DataModel.Channel;
using Daqifi.Desktop.DataModel.Network;
using Daqifi.Desktop.IO.Messages;
using Daqifi.Desktop.IO.Messages.Consumers;
using Daqifi.Desktop.IO.Messages.Decoders;
using Daqifi.Desktop.Models;
using System.Text.RegularExpressions;
using System.Globalization;
using System.IO;
using Daqifi.Desktop.IO.Messages.Producers;
using ScpiMessageProducer = Daqifi.Core.Communication.Producers.ScpiMessageProducer;
using System.Runtime.InteropServices; // Added for P/Invoke
using CommunityToolkit.Mvvm.ComponentModel; // Added using
using System.Threading;

namespace Daqifi.Desktop.Device;

// Added NativeMethods class for P/Invoke
internal static partial class NativeMethods // Marked as partial
{
    // Prevents the system from entering sleep or hibernation.
    public const uint EsSystemRequired = 0x00000001;
    // Informs the system that the state should remain in effect until another call resets it.
    public const uint EsContinuous = 0x80000000;

    [LibraryImport("kernel32.dll", SetLastError = true)] // Changed to LibraryImport
    public static partial uint SetThreadExecutionState(uint esFlags); // Marked as partial
}

// Changed base class and added partial keyword
public abstract partial class AbstractStreamingDevice : ObservableObject, IStreamingDevice
{
    private enum MessageHandlerType
    {
        Status,
        Streaming,
        SdCard
    }
        
    public abstract ConnectionType ConnectionType { get; }
        
    private const double TickPeriod = 20E-9f;
    
    // Converted StreamingFrequency property to [ObservableProperty] field
    [ObservableProperty]
    private int _streamingFrequency = 1;

    private readonly Dictionary<string, DateTime> _previousTimestamps = new();
    private readonly Dictionary<string, uint?> _previousDeviceTimestamps = new();
    private List<SdCardFile> _sdCardFiles = [];

    #region Properties

    protected readonly AppLogger AppLogger = AppLogger.Instance;

    public DeviceMode Mode { get; private set; } = DeviceMode.StreamToApp;

    public bool IsLoggingToSdCard { get; private set; }

    public IReadOnlyList<SdCardFile> SdCardFiles => _sdCardFiles.AsReadOnly();

    public int Id { get; set; }

    public string Name { get; set; }
    public string MacAddress { get; set; }

    public string DevicePartNumber { get; private set; } = string.Empty;

    public string DeviceSerialNo { get; set; } = string.Empty;

    public string DeviceVersion { get; set; }

    public string IpAddress { get; set; } = string.Empty;
    
    // Removed original StreamingFrequency property definition

    public NetworkConfiguration NetworkConfiguration { get; set; } = new();

    public IMessageConsumer MessageConsumer { get; set; }
    public IMessageProducer MessageProducer { get; set; }
    public List<IChannel> DataChannels { get; set; } = [];

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
        MessageConsumer.OnMessageReceived -= HandleStatusMessageReceived;
        MessageConsumer.OnMessageReceived -= HandleStreamingMessageReceived;
        MessageConsumer.OnMessageReceived -= HandleSdCardMessageReceived;

        // Add the new handler
        switch (handlerType)
        {
            case MessageHandlerType.Status:
                MessageConsumer.OnMessageReceived += HandleStatusMessageReceived;
                break;
            case MessageHandlerType.Streaming:
                MessageConsumer.OnMessageReceived += HandleStreamingMessageReceived;
                break;
            case MessageHandlerType.SdCard:
                MessageConsumer.OnMessageReceived += HandleSdCardMessageReceived;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(handlerType), handlerType, null);
        }
            
        AppLogger.Information($"Message handler set to: {handlerType}");
    }

    private void HandleStatusMessageReceived(object sender, MessageEventArgs<object> e)
    {
        var message = e.Message.Data as DaqifiOutMessage;
        if (message == null || !IsValidStatusMessage(message))
        {
            MessageProducer.Send(ScpiMessageProducer.GetDeviceInfo);
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

    private void HandleStreamingMessageReceived(object sender, MessageEventArgs<object> e)
    {
        if (!IsStreaming)
        {
            return;
        }
        
        var message = e.Message.Data as DaqifiOutMessage;
        if (message == null)
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

        var previousTimestamp = _previousTimestamps[deviceId];
        var previousDeviceTimestamp = _previousDeviceTimestamps[deviceId].GetValueOrDefault();

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
        var hasDigitalData = message.DigitalData.Length > 0;
        var hasAnalogData = message.AnalogInData.Count > 0;

        if (hasDigitalData)
        {
            digitalData1 = message.DigitalData.ElementAtOrDefault(0);
            digitalData2 = message.DigitalData.ElementAtOrDefault(1);
        }

        // Loop through channels for this device
        foreach (var channel in DataChannels.Where(c => c.IsActive))
        {
            try
            {
                if (channel.Type == ChannelType.Digital && hasDigitalData)
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

                    // Assign the sample for the digital input channel
                    if (channel.Direction == ChannelDirection.Input)
                    {
                        channel.ActiveSample = new DataSample(this, channel, messageTimestamp, Convert.ToInt32(bit));
                    }
                    digitalCount++;
                }
                else if (channel.Type == ChannelType.Analog && hasAnalogData)
                {
                    if (analogCount >= message.AnalogInData.Count)
                    {
                        AppLogger.Error("Trying to access more analog channels than received data. " +
                                        $"Expected {analogCount} but messaged had {message.AnalogInData.Count} ");
                        break;
                    }

                    // Process the analog sample
                    var sample = new DataSample(this, channel, messageTimestamp, ScaleAnalogSample(channel as AnalogChannel, message.AnalogInData.ElementAt(analogCount)));
                    channel.ActiveSample = sample;
                    analogCount++;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Error processing channel data: {ex.Message}");
            }
        }

        var deviceMessage = new DeviceMessage
        {
            DeviceName = Name,
            AnalogChannelCount = analogCount,
            DeviceSerialNo = message.DeviceSn.ToString(),
            DeviceVersion = message.DeviceFwRev,
            DigitalChannelCount = digitalCount,
            TimestampTicks = messageTimestamp.Ticks,
            AppTicks = DateTime.Now.Ticks,
            DeviceStatus = (int)message.DeviceStatus,
            BatteryStatus = (int)message.BattStatus,
            PowerStatus = (int)message.PwrStatus,
            TempStatus = message.TempStatus,
            TargetFrequency = (int)message.TimestampFreq,
            Rollover = rollover,
        };

        Logger.LoggingManager.Instance.HandleDeviceMessage(this, deviceMessage);

        _previousTimestamps[deviceId] = messageTimestamp;
        _previousDeviceTimestamps[deviceId] = message.MsgTimeStamp;
    }

    private void HandleSdCardMessageReceived(object sender, MessageEventArgs<object> e)
    {
        // Cast the data to the expected type
        var response = e.Message.Data as string;
        if (response == null)
        {
            AppLogger.Warning("Expected string response for SD card operation");
            return;
        }

        try
        {
            // Check if this is a file list response (contains multiple lines with .bin files)
            if (response.Contains(".bin"))
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
            .Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries)
            .Select(path =>
            {
                var cleanPath = path.Trim();

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
                    out var result))
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
        if (newMode == DeviceMode.LogToDevice && ConnectionType != ConnectionType.Usb)
        {
            throw new InvalidOperationException("SD Card logging is only available when connected via USB");
        }

        if (Mode == newMode)
        {
            return;
        }
            
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
        switch (Mode)
        {
            case DeviceMode.StreamToApp:
                StopMessageConsumer();
                break;
            case DeviceMode.LogToDevice:
                // Ensure SD card logging is stopped
                StopSdCardLogging();
                break;
        }

        Mode = newMode;

        // Setup new mode
        switch (Mode)
        {
            case DeviceMode.StreamToApp:
                PrepareLanInterface();
                break;
            case DeviceMode.LogToDevice:
                PrepareSdInterface();
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        OnPropertyChanged(nameof(Mode));
    }

    public void StartSdCardLogging()
    {
        if (ConnectionType != ConnectionType.Usb)
        {
            throw new InvalidOperationException("SD Card logging is only available when connected via USB");
        }

        if (Mode != DeviceMode.LogToDevice)
        {
            throw new InvalidOperationException("Cannot start SD card logging while in StreamToApp mode");
        }

        try
        {
            MessageProducer.Send(ScpiMessageProducer.EnableStorageSd);
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
                
            IsLoggingToSdCard = true;
            IsStreaming = true; // We're streaming to SD card
            AppLogger.Information($"Enabled SD card logging for device {DeviceSerialNo}");
            OnPropertyChanged(nameof(IsLoggingToSdCard));
            OnPropertyChanged(nameof(IsStreaming));
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
            MessageProducer.Send(ScpiMessageProducer.DisableStorageSd);
                
            IsLoggingToSdCard = false;
            IsStreaming = false;
            AppLogger.Information($"Disabled SD card logging for device {DeviceSerialNo}");
            OnPropertyChanged(nameof(IsLoggingToSdCard));
            OnPropertyChanged(nameof(IsStreaming));
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"Failed to disable SD card logging for device {DeviceSerialNo}");
            throw;
        }
    }

    public void RefreshSdCardFiles()
    {
        if (ConnectionType != ConnectionType.Usb)
        {
            throw new InvalidOperationException("SD Card access is only available when connected via USB");
        }

        var stream = MessageConsumer.DataStream;
            
        // Stop existing consumer first
        if (MessageConsumer != null && MessageConsumer.Running)
        {
            MessageConsumer.Stop();
        }

        // Create and start the new consumer BEFORE sending any commands
        MessageConsumer = new TextMessageConsumer(stream);
        SetMessageHandler(MessageHandlerType.SdCard);
        MessageConsumer.Start();
            
        // Give the consumer a moment to fully initialize
        Thread.Sleep(50);

        // Now that we're listening, prepare the SD interface
        PrepareSdInterface();
            
        // Give the interface time to switch and send any responses
        Thread.Sleep(100);
            
        // Now request the file list
        MessageProducer.Send(ScpiMessageProducer.GetSdFileList);
    }

    public void UpdateSdCardFiles(List<SdCardFile> files)
    {
        _sdCardFiles = files;
        OnPropertyChanged(nameof(SdCardFiles));
    }

    public void InitializeStreaming()
    {
        if (IsStreaming) return;

        // Prevent computer sleep when starting streaming
        var previousState = NativeMethods.SetThreadExecutionState(NativeMethods.EsContinuous | NativeMethods.EsSystemRequired);
        if (previousState == 0) // Check for failure (returns 0 on failure)
        {
            AppLogger.Warning("Failed to set computer from sleeping while streaming.");
        }
        else
        {
            AppLogger.Information("Preventing computer sleep during streaming.");
        }

        if (Mode != DeviceMode.StreamToApp)
        {
            throw new InvalidOperationException("Cannot initialize streaming while in LogToDevice mode");
        }
            
        MessageProducer.Send(ScpiMessageProducer.StartStreaming(StreamingFrequency));
        IsStreaming = true;
        StartStreamingMessageConsumer();
    }

    public void StopStreaming()
    {
        if (!IsStreaming) return;

        // Allow computer sleep again when stopping streaming
        var previousState = NativeMethods.SetThreadExecutionState(NativeMethods.EsContinuous);
        if (previousState == 0) // Check for failure
        {
            AppLogger.Warning("Failed to reset thread execution state to allow sleep.");
        }
        else
        {
            AppLogger.Information("Allowing computer sleep after streaming.");
        }

        IsStreaming = false;
        MessageProducer.Send(ScpiMessageProducer.StopStreaming);
        StopMessageConsumer();

        foreach (var channel in DataChannels)
        {
            if (channel.ActiveSample != null)
            {
                channel.ActiveSample = null;
            }
        }
    }

    protected void StartStreamingMessageConsumer()
    {
        if (Mode != DeviceMode.StreamToApp)
        {
            return; // Don't start consumer if not in streaming mode
        }

        // Always create a new message consumer to ensure clean state
        var stream = MessageConsumer?.DataStream;
        if (stream != null)
        {
            // Stop and cleanup existing consumer if any
            MessageConsumer?.Stop();

            // Create new consumer
            MessageConsumer = new MessageConsumer(stream);
            if (MessageConsumer is MessageConsumer msgConsumer)
            {
                msgConsumer.ClearBuffer();
            }
                
            SetMessageHandler(MessageHandlerType.Streaming);
            MessageConsumer.Start();
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
        MessageProducer.Send(ScpiMessageProducer.DisableDeviceEcho);
    }

    protected void TurnDeviceOn()
    {
        MessageProducer.Send(ScpiMessageProducer.TurnDeviceOn);
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

    private void PopulateAnalogInChannels(DaqifiOutMessage message)
    {
        if (message.AnalogInPortNum == 0) { return; }

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
            DeviceVersion = message.DeviceFwRev;
        }
        if (message.IpAddr != null && message.IpAddr.Length > 0)
        {
            IpAddress = string.Join(",", message.IpAddr);
        }
        if (message.MacAddr.Length > 0)
        {
            MacAddress = ProtobufDecoder.GetMacAddressString(message);
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
        MessageProducer.Send(ScpiMessageProducer.GetDeviceInfo);
    }

    private static double ScaleAnalogSample(AnalogChannel channel, double analogValue)
    {
        return (analogValue / channel.Resolution) * channel.PortRange * channel.CalibrationMValue *
            channel.InternalScaleMValue + channel.CalibrationBValue;
    }

    public void UpdateNetworkConfiguration()
    {
        if (IsStreaming) { StopStreaming(); }

        // Ensure WiFi is enabled before configuring it
        // SD and WiFi share the same SPI bus, so disable SD first
        MessageProducer.Send(ScpiMessageProducer.DisableStorageSd);
        MessageProducer.Send(ScpiMessageProducer.EnableNetworkLan);

        switch (NetworkConfiguration.Mode)
        {
            case WifiMode.ExistingNetwork:
                MessageProducer.Send(ScpiMessageProducer.SetNetworkWifiModeExisting);
                break;
            case WifiMode.SelfHosted:
                MessageProducer.Send(ScpiMessageProducer.SetNetworkWifiModeSelfHosted);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
        
        MessageProducer.Send(ScpiMessageProducer.SetNetworkWifiSsid(NetworkConfiguration.Ssid));

        switch (NetworkConfiguration.SecurityType)
        {
            case WifiSecurityType.None:
                MessageProducer.Send(ScpiMessageProducer.SetNetworkWifiSecurityOpen);
                break;
            case WifiSecurityType.WpaPskPhrase:
                MessageProducer.Send(ScpiMessageProducer.SetNetworkWifiSecurityWpa);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
        
        MessageProducer.Send(ScpiMessageProducer.SetNetworkWifiPassword(NetworkConfiguration.Password));
        MessageProducer.Send(ScpiMessageProducer.ApplyNetworkLan);
        
        // Wait for WiFi module to restart after applying settings
        Thread.Sleep(2000);
        
        // Re-enable WiFi after the module restarts, but only if we're in StreamToApp mode
        // The ApplyNetworkLan command causes the WiFi module to restart,
        // so we need to re-enable it after the restart completes.
        // SD and WiFi share the same SPI bus and cannot be enabled simultaneously.
        if (Mode == DeviceMode.StreamToApp)
        {
            MessageProducer.Send(ScpiMessageProducer.EnableNetworkLan);
        }
        
        MessageProducer.Send(ScpiMessageProducer.SaveNetworkLan);
    }

    public void Reboot()
    {
        MessageProducer.Send(ScpiMessageProducer.RebootDevice);
        MessageProducer.StopSafely();
        MessageConsumer.Stop();
    }

    // SD and LAN can't both be enabled due to hardware limitations
    private void PrepareSdInterface()
    {
        MessageProducer.Send(ScpiMessageProducer.DisableNetworkLan);
        MessageProducer.Send(ScpiMessageProducer.EnableStorageSd);
    }
        
    // SD and LAN can't both be enabled due to hardware limitations
    private void PrepareLanInterface()
    {
        MessageProducer.Send(ScpiMessageProducer.DisableStorageSd);
        MessageProducer.Send(ScpiMessageProducer.EnableNetworkLan);
    }
}