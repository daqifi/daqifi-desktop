using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Core.Device.Network;
using ChannelDirection = Daqifi.Core.Channel.ChannelDirection;
using ChannelType = Daqifi.Core.Channel.ChannelType;
using Daqifi.Desktop.IO.Messages;
using Daqifi.Desktop.Models;
using System.Globalization;
using System.IO;
using ScpiMessageProducer = Daqifi.Core.Communication.Producers.ScpiMessageProducer;
using System.Runtime.InteropServices; // Added for P/Invoke
using CommunityToolkit.Mvvm.ComponentModel; // Added using
using Daqifi.Core.Device; // Added for DeviceType, DeviceTypeDetector, DeviceMetadata, DeviceCapabilities, DeviceState
using Daqifi.Core.Device.Protocol; // Added for ProtobufProtocolHandler
using Daqifi.Core.Communication.Messages; // Added for IInboundMessage
using CoreStreamingDevice = Daqifi.Core.Device.DaqifiStreamingDevice;
using Daqifi.Core.Device.SdCard;
using CoreSdCardFileInfo = Daqifi.Core.Device.SdCard.SdCardFileInfo;

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
    public abstract ConnectionType ConnectionType { get; }

    // Converted StreamingFrequency property to [ObservableProperty] field
    [ObservableProperty]
    private int _streamingFrequency = 1;

    // DeviceType property with default value of Unknown
    [ObservableProperty]
    private DeviceType _deviceType = DeviceType.Unknown;

    // DeviceState property for tracking device state
    [ObservableProperty]
    private DeviceState _deviceState = DeviceState.Disconnected;

    private readonly ITimestampProcessor _timestampProcessor = new TimestampProcessor();
    private List<SdCardFile> _sdCardFiles = [];

    // Protocol handler for automatic message routing
    private IProtocolHandler? _protocolHandler;

    /// <summary>
    /// Core streaming device used for SD card operations (USB devices only).
    /// </summary>
    protected virtual CoreStreamingDevice? CoreDeviceForSd => null;

    /// <summary>
    /// Core streaming device used for live stream start/stop operations.
    /// </summary>
    protected virtual CoreStreamingDevice? CoreDeviceForStreaming => null;

    #region Properties

    protected readonly AppLogger AppLogger = AppLogger.Instance;

    /// <summary>
    /// Gets the device metadata from Core library
    /// </summary>
    public DeviceMetadata Metadata { get; } = new DeviceMetadata();

    /// <summary>
    /// Gets the device capabilities from Core's metadata
    /// </summary>
    public DeviceCapabilities Capabilities => Metadata.Capabilities;

    public DeviceMode Mode { get; private set; } = DeviceMode.StreamToApp;

    public bool IsLoggingToSdCard { get; private set; }

    public IReadOnlyList<SdCardFile> SdCardFiles => _sdCardFiles.AsReadOnly();

    public int Id { get; set; }

    public string Name { get; set; }
    public string MacAddress { get; set; }

    public string DevicePartNumber { get; private set; } = string.Empty;

    public string DeviceSerialNo { get; set; } = string.Empty;

    public string DeviceVersion { get; set; }

    private string _ipAddress = string.Empty;
    
    public string IpAddress 
    { 
        get => _ipAddress; 
        set 
        { 
            if (_ipAddress != value)
            {
                _ipAddress = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayIdentifier));
            }
        } 
    }

    /// <summary>
    /// Gets the appropriate display identifier for this device based on connection type.
    /// Returns COM port for USB devices, IP address for WiFi devices.
    /// </summary>
    public virtual string DisplayIdentifier => ConnectionType switch
    {
        ConnectionType.Usb => GetUsbDisplayIdentifier(),
        ConnectionType.Wifi => IpAddress,
        _ => "Unknown"
    };

    /// <summary>
    /// Gets the COM port identifier for USB devices. Override in derived classes.
    /// </summary>
    /// <returns>COM port name for USB devices</returns>
    protected virtual string GetUsbDisplayIdentifier()
    {
        return "USB";
    }

    // Removed original StreamingFrequency property definition

    public NetworkConfiguration NetworkConfiguration { get; set; } = new();

    public List<IChannel> DataChannels { get; set; } = [];

    /// <summary>
    /// Gets whether the device is currently connected.
    /// Override in derived classes to provide accurate connection state.
    /// </summary>
    public virtual bool IsConnected => DeviceState == DeviceState.Connected || DeviceState == DeviceState.Ready;

    public bool IsStreaming { get; set; }
    public bool IsFirmwareOutdated { get; set; }

    /// <summary>
    /// Gets the device's hardware timestamp clock frequency in Hz.
    /// Populated from the device's status message (<c>TimestampFreq</c> field).
    /// Used as a fallback when parsing SD card files that lack this field.
    /// </summary>
    public uint TimestampFrequency { get; private set; }

    // Debug mode properties
    public bool IsDebugModeEnabled { get; private set; }
    public event Action<DebugDataModel>? DebugDataReceived;
    #endregion

    protected virtual bool RequestDeviceInfoOnInitialize => true;

    #region Abstract Methods
    public abstract bool Connect();

    public abstract bool Disconnect();

    public abstract bool Write(string command);

    /// <summary>
    /// Sends a message to the device. Must be implemented in derived classes.
    /// Core-based devices use DaqifiDevice.Send() for sending messages.
    /// </summary>
    /// <param name="message">The SCPI message to send.</param>
    protected abstract void SendMessage(IOutboundMessage<string> message);
    #endregion

    #region Message Handlers
    /// <summary>
    /// Initializes the protocol handler for automatic message routing.
    /// Uses Core's ProtobufProtocolHandler to route status, streaming, and SD card messages.
    /// </summary>
    private void InitializeProtocolHandler()
    {
        _protocolHandler = new ProtobufProtocolHandler(
            statusMessageHandler: OnStatusMessageReceived,
            streamMessageHandler: OnStreamMessageReceived,
            sdCardMessageHandler: _ => { } // SD card messages are text-based, handled separately; empty handler prevents NullReferenceException
        );

        AppLogger.Information("Protocol handler initialized with automatic message routing");
    }

    /// <summary>
    /// Routes incoming messages through the protocol handler.
    /// This method is called by the message consumer for each received message.
    /// </summary>
    private void OnInboundMessageReceived(object sender, MessageEventArgs<object> e)
    {
        // Convert Desktop's message format to Core's format
        var inboundMessage = new GenericInboundMessage<object>(e.Message.Data);

        // Route through protocol handler if available and it can handle this message
        if (_protocolHandler != null && _protocolHandler.CanHandle(inboundMessage))
        {
            // Fire and forget - we don't need to wait for the handler to complete
            _ = _protocolHandler.HandleAsync(inboundMessage);
        }
    }

    /// <summary>
    /// Handles status messages received from the device during initialization.
    /// Called automatically by ProtobufProtocolHandler when a status message is detected.
    /// </summary>
    private void OnStatusMessageReceived(DaqifiOutMessage message)
    {
        // Protocol handler already validated this is a status message via IsStatusMessage()
        // No need to revalidate here - just process it

        HydrateDeviceMetadata(message);
        PopulateChannelsFromCore(message);
        PopulateAnalogOutChannels(message);

        AppLogger.Information("Status message processed - device metadata populated");
    }

    /// <summary>
    /// Handles streaming messages received from the device.
    /// Called automatically by ProtobufProtocolHandler when a streaming message is detected.
    /// </summary>
    private void OnStreamMessageReceived(DaqifiOutMessage message)
    {
        if (!IsStreaming || Mode == DeviceMode.LogToDevice)
        {
            return;
        }

        // Protocol handler already validated this is a streaming message with timestamp
        // No need to revalidate here

        var deviceId = message.DeviceSn.ToString(CultureInfo.InvariantCulture);

        // Use Core's TimestampProcessor for rollover handling
        var timestampResult = _timestampProcessor.ProcessTimestamp(deviceId, message.MsgTimeStamp);
        var messageTimestamp = timestampResult.Timestamp;
        var rollover = timestampResult.WasRollover;

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

        // Process analog channels - device sends data in channel index order, not activation order
        if (hasAnalogData)
        {
            try
            {
                var activeAnalogChannels = DataChannels.Where(c => c.IsActive && c.Type == ChannelType.Analog)
                                                      .Cast<AnalogChannel>()
                                                      .OrderBy(c => c.Index)
                                                      .ToList();


                for (var dataIndex = 0; dataIndex < message.AnalogInData.Count && dataIndex < activeAnalogChannels.Count; dataIndex++)
                {
                    var channel = activeAnalogChannels[dataIndex];
                    var rawValue = message.AnalogInData[dataIndex];
                    // Use core's scaling implementation for correct formula and thread-safety
                    var scaledValue = channel.GetScaledValue((int)rawValue);
                    var sample = new DataSample(this, channel, messageTimestamp, scaledValue);
                    channel.ActiveSample = sample;
                }

                if (message.AnalogInData.Count != activeAnalogChannels.Count)
                {
                    AppLogger.Warning($"[CHANNEL_MAPPING] Analog data count mismatch: received {message.AnalogInData.Count} data points for {activeAnalogChannels.Count} active channels");
                }

                // Send debug data if debug mode is enabled
                SendDebugData(message, activeAnalogChannels, messageTimestamp);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[CHANNEL_MAPPING] Error processing analog channel data: {ex.Message}");
            }
        }

        // Process digital channels - device sends data in channel index order, not activation order
        if (hasDigitalData)
        {
            try
            {
                var activeDigitalChannels = DataChannels.Where(c => c.IsActive && c.Type == ChannelType.Digital)
                                                       .OrderBy(c => c.Index)
                                                       .ToList();

                for (var dataIndex = 0; dataIndex < activeDigitalChannels.Count; dataIndex++)
                {
                    var channel = activeDigitalChannels[dataIndex];

                    bool bit;
                    if (dataIndex < 8)
                    {
                        bit = (digitalData1 & (1 << dataIndex)) != 0;
                    }
                    else
                    {
                        bit = (digitalData2 & (1 << (dataIndex % 8))) != 0;
                    }

                    // Assign the sample for the digital input channel
                    if (channel.Direction == ChannelDirection.Input)
                    {
                        channel.ActiveSample = new DataSample(this, channel, messageTimestamp, Convert.ToInt32(bit));
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Error processing digital channel data: {ex.Message}");
            }
        }

        var deviceMessage = new DeviceMessage
        {
            DeviceName = Name,
            AnalogChannelCount = analogCount,
            DeviceSerialNo = message.DeviceSn.ToString(CultureInfo.InvariantCulture),
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
    }

    /// <summary>
    /// Routes a message through the protocol handler for processing.
    /// Called by derived classes (e.g., WiFi devices) that receive messages directly from Core's DaqifiDevice.
    /// </summary>
    /// <param name="e">The message event args containing the protobuf message.</param>
    protected void HandleInboundMessage(MessageEventArgs<object> e)
    {
        OnInboundMessageReceived(this, e);
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
            // Enable active channels — build a single bitmask for all analog channels
            // (each EnableAdcChannels call replaces the previous setting, so we must send all at once)
            var analogChannelMask = 0u;
            var hasDigitalChannels = false;

            foreach (var channel in DataChannels.Where(c => c.IsActive))
            {
                if (channel.Type == ChannelType.Analog)
                {
                    analogChannelMask |= 1u << channel.Index;
                }
                else if (channel.Type == ChannelType.Digital)
                {
                    hasDigitalChannels = true;
                }
            }

            if (analogChannelMask != 0)
            {
                SendMessage(ScpiMessageProducer.EnableAdcChannels(
                    analogChannelMask.ToString(CultureInfo.InvariantCulture)));
            }

            if (hasDigitalChannels)
            {
                SendMessage(ScpiMessageProducer.EnableDioPorts());
            }

            var coreDevice = GetCoreDeviceForSd();
            coreDevice.StreamingFrequency = StreamingFrequency;
            coreDevice.StartSdCardLoggingAsync().GetAwaiter().GetResult();

            IsLoggingToSdCard = coreDevice.IsLoggingToSdCard;
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
        if (ConnectionType != ConnectionType.Usb)
        {
            throw new InvalidOperationException("SD Card logging is only available when connected via USB");
        }

        try
        {
            var coreDevice = GetCoreDeviceForSd();
            coreDevice.StopSdCardLoggingAsync().GetAwaiter().GetResult();

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

        EnsureSdOperationsQuiesced();

        var coreDevice = GetCoreDeviceForSd();
        var files = coreDevice.GetSdCardFilesAsync().GetAwaiter().GetResult();

        // Some firmware/transport timings can yield a transient SCPI error line in list output.
        // Retry once after a short settle delay before surfacing results.
        if (ContainsScpiErrorEntry(files))
        {
            AppLogger.Warning($"SD card list returned a SCPI error for device {DeviceSerialNo}. Retrying once.");
            Thread.Sleep(200);
            files = coreDevice.GetSdCardFilesAsync().GetAwaiter().GetResult();
        }

        UpdateSdCardFiles(MapSdCardFiles(files));
    }

    public void UpdateSdCardFiles(List<SdCardFile> files)
    {
        _sdCardFiles = files;
        OnPropertyChanged(nameof(SdCardFiles));
    }

    private CoreStreamingDevice GetCoreDeviceForSd()
    {
        var coreDevice = CoreDeviceForSd;
        if (coreDevice == null)
        {
            throw new InvalidOperationException("Core SD card operations are not available for this device.");
        }

        return coreDevice;
    }

    public async Task<SdCardDownloadResult> DownloadSdCardFileAsync(
        string fileName,
        IProgress<SdCardTransferProgress>? progress = null,
        CancellationToken ct = default)
    {
        EnsureSdOperationsQuiesced();

        var coreDevice = GetCoreDeviceForSd();
        return await coreDevice.DownloadSdCardFileAsync(fileName, progress, ct);
    }

    public async Task DeleteSdCardFileAsync(string fileName, CancellationToken ct = default)
    {
        EnsureSdOperationsQuiesced();

        var coreDevice = GetCoreDeviceForSd();
        await coreDevice.DeleteSdCardFileAsync(fileName, ct);
    }

    private static List<SdCardFile> MapSdCardFiles(IEnumerable<CoreSdCardFileInfo> files)
    {
        return files.Select(file => new SdCardFile
            {
                FileName = file.FileName,
                CreatedDate = file.CreatedDate ?? DateTime.MinValue
            })
            .Where(file => IsImportableSdLogFileName(file.FileName))
            .ToList();
    }

    private void EnsureSdOperationsQuiesced()
    {
        if (IsLoggingToSdCard)
        {
            StopSdCardLogging();
            return;
        }

        if (IsStreaming)
        {
            StopStreaming();
        }
    }

    private static bool ContainsScpiErrorEntry(IEnumerable<CoreSdCardFileInfo> files)
    {
        return files.Any(file => IsScpiErrorLine(file.FileName));
    }

    private static bool IsImportableSdLogFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (IsScpiErrorLine(fileName))
        {
            return false;
        }

        if (fileName.Any(char.IsControl))
        {
            return false;
        }

        var normalizedName = Path.GetFileName(fileName);
        return normalizedName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsScpiErrorLine(string value)
    {
        return value.StartsWith("**ERROR", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase);
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

        var coreStreamingDevice = GetCoreDeviceForStreaming();
        coreStreamingDevice.StreamingFrequency = StreamingFrequency;
        coreStreamingDevice.StartStreaming();
        IsStreaming = coreStreamingDevice.IsStreaming;
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

        var coreStreamingDevice = GetCoreDeviceForStreaming();
        coreStreamingDevice.StopStreaming();
        IsStreaming = coreStreamingDevice.IsStreaming;

        // Reset timestamp processor state for clean restart
        _timestampProcessor.ResetAll();

        foreach (var channel in DataChannels)
        {
            if (channel.ActiveSample != null)
            {
                channel.ActiveSample = null;
            }
        }
    }

    private CoreStreamingDevice GetCoreDeviceForStreaming()
    {
        var coreDevice = CoreDeviceForStreaming;
        if (coreDevice == null)
        {
            throw new InvalidOperationException("Core live streaming operations are not available for this device.");
        }

        return coreDevice;
    }


    protected void TurnOffEcho()
    {
        SendMessage(ScpiMessageProducer.DisableDeviceEcho);
    }

    protected void TurnDeviceOn()
    {
        SendMessage(ScpiMessageProducer.TurnDeviceOn);
    }

    protected void SetProtobufMessageFormat()
    {
        SendMessage(ScpiMessageProducer.SetProtobufStreamFormat);
    }

    /// <summary>
    /// Initializes the device using the standard initialization sequence with proper delays.
    /// This async method provides non-blocking initialization similar to Core's approach.
    /// </summary>
    protected virtual async Task InitializeDeviceAsync()
    {
        TurnOffEcho();
        await Task.Delay(100);  // Device needs time to process

        SendMessage(ScpiMessageProducer.StopStreaming);
        await Task.Delay(100);

        TurnDeviceOn();
        await Task.Delay(100);

        SetProtobufMessageFormat();
        await Task.Delay(100);
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
                var channelSetByte = 0u; // Use unsigned int to handle higher channel numbers

                // Get Exsiting Channel Set Byte
                foreach (var activeChannel in activeAnalogChannels)
                {
                    channelSetByte = channelSetByte | (1u << activeChannel.Index);
                }

                // Add Channel Bit to the Channel Set Byte
                channelSetByte = channelSetByte | (1u << channel.Index);

                // Convert to a string
                var channelSetString = Convert.ToString(channelSetByte);

                // Send the command to add the channel
                SendMessage(ScpiMessageProducer.EnableAdcChannels(channelSetString));
                break;
            case ChannelType.Digital:
                SendMessage(ScpiMessageProducer.EnableDioPorts());
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
                var channelSetByte = 0u; // Use unsigned int to handle higher channel numbers

                //Get Exsiting Channel Set Byte
                foreach (var activeChannel in activeAnalogChannels)
                {
                    channelSetByte = channelSetByte | (1u << activeChannel.Index);
                }

                //Remove Channel Bit from the Channel Set Byte
                channelSetByte = channelSetByte & ~(1u << channelToRemove.Index);

                //Convert to a string
                var channelSetString = Convert.ToString(channelSetByte);

                //Send the command to remove the channel
                SendMessage(ScpiMessageProducer.EnableAdcChannels(channelSetString));
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
                SendMessage(ScpiMessageProducer.SetDioPortState(channel.Index, value));
                break;
        }
    }

    public void SetChannelDirection(IChannel channel, ChannelDirection direction)
    {
        switch (direction)
        {
            case ChannelDirection.Input:
                SendMessage(ScpiMessageProducer.SetDioPortDirection(channel.Index, 0));
                break;
            case ChannelDirection.Output:
                SendMessage(ScpiMessageProducer.SetDioPortDirection(channel.Index, 1));
                break;
        }
    }

    /// <summary>
    /// Populates channels using Core's DaqifiDevice.PopulateChannelsFromStatus method.
    /// This leverages Core's channel creation logic and wraps the resulting channels
    /// with Desktop-specific wrappers that add UI features (colors, expressions, database).
    /// </summary>
    /// <param name="message">The protobuf status message containing channel configuration.</param>
    private void PopulateChannelsFromCore(DaqifiOutMessage message)
    {
        try
        {
            // Use Core's DaqifiDevice to populate channels from the status message
            var coreDevice = new DaqifiDevice(Name ?? "Unknown");
            coreDevice.PopulateChannelsFromStatus(message);

            // Clear existing channels before repopulating to prevent duplicates (Issue #29)
            DataChannels.Clear();

            // Wrap Core channels with Desktop-specific wrappers
            foreach (var coreChannel in coreDevice.Channels)
            {
                if (coreChannel is Daqifi.Core.Channel.IAnalogChannel coreAnalogChannel)
                {
                    DataChannels.Add(new AnalogChannel(this, coreAnalogChannel));
                }
                else if (coreChannel is Daqifi.Core.Channel.IDigitalChannel coreDigitalChannel)
                {
                    // Core sets digital channels to IsEnabled = true by default, keep that behavior
                    DataChannels.Add(new DigitalChannel(this, coreDigitalChannel));
                }
            }

            AppLogger.Information($"Populated {coreDevice.Channels.Count} channels from Core " +
                $"({coreDevice.Channels.Count(c => c.Type == ChannelType.Analog)} analog, " +
                $"{coreDevice.Channels.Count(c => c.Type == ChannelType.Digital)} digital)");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"Failed to populate channels from Core for device {DisplayIdentifier}");
            throw;
        }
    }

    protected void HydrateDeviceMetadata(DaqifiOutMessage message)
    {
        // Use Core's metadata update method
        Metadata.UpdateFromProtobuf(message);

        // Map Core metadata to desktop properties for backward compatibility
        DevicePartNumber = Metadata.PartNumber;
        DeviceSerialNo = Metadata.SerialNumber;
        DeviceVersion = Metadata.FirmwareVersion;
        DeviceType = Metadata.DeviceType;
        IpAddress = Metadata.IpAddress;
        MacAddress = Metadata.MacAddress;

        // Store timestamp frequency for SD card import fallback
        if (message.TimestampFreq != 0)
        {
            TimestampFrequency = message.TimestampFreq;
        }

        // Update NetworkConfiguration from metadata
        if (!string.IsNullOrWhiteSpace(Metadata.Ssid))
        {
            NetworkConfiguration.Ssid = Metadata.Ssid;
        }

        // This now correctly handles security mode 0 (open network) - fixes WiFi bug!
        NetworkConfiguration.SecurityType = (WifiSecurityType)Metadata.WifiSecurityMode;

        if (Metadata.WifiInfrastructureMode > 0)
        {
            NetworkConfiguration.Mode = (WifiMode)Metadata.WifiInfrastructureMode;
        }

        AppLogger.Information($"Detected device type: {DeviceType} from part number: {Metadata.PartNumber}");
    }

    private void PopulateAnalogOutChannels(DaqifiOutMessage message)
    {
        if (message.AnalogOutPortNum == 0) { return; }

        // TODO handle HasAnalogOutPortNum.  Firmware doesn't yet have this field
    }
    #endregion

    public void InitializeDeviceState()
    {
        // Initialize protocol handler for automatic message routing
        InitializeProtocolHandler();

        if (RequestDeviceInfoOnInitialize)
        {
            // Request device info - protocol handler will automatically route the response
            SendMessage(ScpiMessageProducer.GetDeviceInfo);
        }
    }

    public async Task UpdateNetworkConfiguration()
    {
        if (IsStreaming) { StopStreaming(); }

        switch (NetworkConfiguration.Mode)
        {
            case WifiMode.ExistingNetwork:
                SendMessage(ScpiMessageProducer.SetNetworkWifiModeExisting);
                break;
            case WifiMode.SelfHosted:
                SendMessage(ScpiMessageProducer.SetNetworkWifiModeSelfHosted);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        SendMessage(ScpiMessageProducer.SetNetworkWifiSsid(NetworkConfiguration.Ssid));

        switch (NetworkConfiguration.SecurityType)
        {
            case WifiSecurityType.None:
                SendMessage(ScpiMessageProducer.SetNetworkWifiSecurityOpen);
                break;
            case WifiSecurityType.WpaPskPhrase:
                SendMessage(ScpiMessageProducer.SetNetworkWifiSecurityWpa);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        SendMessage(ScpiMessageProducer.SetNetworkWifiPassword(NetworkConfiguration.Password));
        SendMessage(ScpiMessageProducer.ApplyNetworkLan);

        // Wait for WiFi module to restart after applying settings
        await Task.Delay(2000);

        // Re-enable WiFi after the module restarts, but only if we're in StreamToApp mode
        // The ApplyNetworkLan command causes the WiFi module to restart,
        // so we need to re-enable it after the restart completes.
        // SD and WiFi share the same SPI bus and cannot be enabled simultaneously.
        if (Mode == DeviceMode.StreamToApp)
        {
            SendMessage(ScpiMessageProducer.DisableStorageSd);
            SendMessage(ScpiMessageProducer.EnableNetworkLan);
        }

        SendMessage(ScpiMessageProducer.SaveNetworkLan);
    }

    public void Reboot()
    {
        SendMessage(ScpiMessageProducer.RebootDevice);
        Disconnect();
    }

    // SD and LAN can't both be enabled due to hardware limitations
    private void PrepareSdInterface()
    {
        SendMessage(ScpiMessageProducer.DisableNetworkLan);
        SendMessage(ScpiMessageProducer.EnableStorageSd);
    }

    // SD and LAN can't both be enabled due to hardware limitations
    private void PrepareLanInterface()
    {
        SendMessage(ScpiMessageProducer.DisableStorageSd);
        SendMessage(ScpiMessageProducer.EnableNetworkLan);
    }

    #region Debug Mode Methods
    /// <summary>
    /// Sets the debug mode for this device
    /// </summary>
    public void SetDebugMode(bool enabled)
    {
        IsDebugModeEnabled = enabled;
        AppLogger.Information($"[DEBUG_MODE] Device {DeviceSerialNo}: Debug mode {(enabled ? "enabled" : "disabled")}");
    }

    /// <summary>
    /// Creates and sends debug data when in debug mode
    /// </summary>
    private void SendDebugData(DaqifiOutMessage message, List<AnalogChannel> activeChannels, DateTime timestamp)
    {
        if (!IsDebugModeEnabled) return;

        try
        {
            var debugData = new DebugDataModel
            {
                Timestamp = timestamp,
                DeviceId = DeviceSerialNo,
                AnalogDataCount = message.AnalogInData.Count,
                RawAnalogValues = message.AnalogInData.ToList(),
                HasDigitalData = message.DigitalData.Length > 0,
                MessageType = "AnalogData",
                ActiveChannelNames = activeChannels.Select(c => c.Name).ToList(),
                ActiveChannelIndices = activeChannels.Select(c => c.Index).ToList()
            };

            // Calculate current channel enable mask
            var enableMask = 0;
            foreach (var channel in DataChannels.Where(c => c.IsActive && c.Type == ChannelType.Analog))
            {
                enableMask |= (1 << channel.Index);
            }
            debugData.ChannelEnableMask = enableMask.ToString(CultureInfo.InvariantCulture);
            debugData.ChannelEnableBinary = Convert.ToString(enableMask, 2);

            // Calculate scaled values
            debugData.ScaledAnalogValues = [];
            for (var i = 0; i < Math.Min(message.AnalogInData.Count, activeChannels.Count); i++)
            {
                var channel = activeChannels[i];
                var rawValue = message.AnalogInData[i];
                // Use core's scaling implementation for correct formula and thread-safety
                var scaledValue = channel.GetScaledValue((int)rawValue);
                debugData.ScaledAnalogValues.Add(scaledValue);
            }

            // Create data flow mapping
            debugData.DataFlowMapping = new List<string>();
            for (var i = 0; i < Math.Min(message.AnalogInData.Count, activeChannels.Count); i++)
            {
                var channel = activeChannels[i];
                var rawValue = message.AnalogInData[i];
                var scaledValue = debugData.ScaledAnalogValues[i];
                debugData.DataFlowMapping.Add($"data[{i}]={rawValue} → {channel.Name}(idx:{channel.Index})={scaledValue:F3}V");
            }

            // Log the debug information
            AppLogger.Information(debugData.LogSummary);

            // Notify subscribers
            DebugDataReceived?.Invoke(debugData);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[DEBUG_MODE] Error creating debug data: {ex.Message}");
        }
    }
    #endregion
}
