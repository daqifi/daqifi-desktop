using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Core.Communication;
using Daqifi.Core.Device.Network;
using ChannelDirection = Daqifi.Core.Channel.ChannelDirection;
using ChannelType = Daqifi.Core.Channel.ChannelType;
using Daqifi.Desktop.IO.Messages;
using Daqifi.Desktop.Models;
using System.Globalization;
using ScpiMessageProducer = Daqifi.Core.Communication.Producers.ScpiMessageProducer;
using System.Runtime.InteropServices; // Added for P/Invoke
using CommunityToolkit.Mvvm.ComponentModel; // Added using
using Daqifi.Core.Device; // Added for DeviceType, DeviceTypeDetector, DeviceMetadata, DeviceCapabilities, DeviceState
using Daqifi.Core.Device.Protocol; // Added for ProtobufProtocolHandler
using Daqifi.Core.Communication.Messages; // Added for IInboundMessage
using CoreStreamingDevice = Daqifi.Core.Device.DaqifiStreamingDevice;
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
    /// Core streaming device used for network configuration orchestration.
    /// </summary>
    protected virtual CoreStreamingDevice? CoreDeviceForNetworkConfiguration => null;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayIdentifier))]
    private string _name = string.Empty;

    /// <summary>Gets or sets the device MAC address. Backed by <see cref="Metadata"/>.</summary>
    public string MacAddress
    {
        get => Metadata.MacAddress;
        set { if (Metadata.MacAddress != value) { Metadata.MacAddress = value; OnPropertyChanged(); } }
    }

    /// <summary>Gets the device part number. Backed by <see cref="Metadata"/>.</summary>
    public string DevicePartNumber => Metadata.PartNumber;

    /// <summary>Gets or sets the device serial number. Backed by <see cref="Metadata"/>.</summary>
    public string DeviceSerialNo
    {
        get => Metadata.SerialNumber;
        set { if (Metadata.SerialNumber != value) { Metadata.SerialNumber = value; OnPropertyChanged(); } }
    }

    /// <summary>Gets or sets the firmware version. Backed by <see cref="Metadata"/>.</summary>
    public string DeviceVersion
    {
        get => Metadata.FirmwareVersion;
        set { if (Metadata.FirmwareVersion != value) { Metadata.FirmwareVersion = value; OnPropertyChanged(); } }
    }

    /// <summary>Gets or sets the IP address. Backed by <see cref="Metadata"/>.</summary>
    public string IpAddress
    {
        get => Metadata.IpAddress;
        set
        {
            if (Metadata.IpAddress != value)
            {
                Metadata.IpAddress = value;
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

    // Debug mode properties
    public bool IsDebugModeEnabled { get; private set; }
    public event Action<DebugDataModel>? DebugDataReceived;
    #endregion

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
    /// Uses Core's ProtobufProtocolHandler to route streaming protobuf messages while Core
    /// itself owns status parsing and channel population.
    /// </summary>
    private void InitializeProtocolHandler()
    {
        _protocolHandler = new ProtobufProtocolHandler(
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
    /// Handles streaming messages received from the device.
    /// Called automatically by ProtobufProtocolHandler when a streaming message is detected.
    /// </summary>
    private void OnStreamMessageReceived(DaqifiOutMessage message)
    {
        if (!IsStreaming || Mode != DeviceMode.StreamToApp)
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
        // USB firmware sends pre-scaled floats (AnalogInDataFloat); WiFi sends raw ADC counts (AnalogInData).
        var hasAnalogData = message.AnalogInData.Count > 0 || message.AnalogInDataFloat.Count > 0;



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

                // USB firmware sends pre-scaled float values (already in volts); use them directly.
                // WiFi firmware sends raw integer ADC counts; apply channel calibration scaling.
                var hasFloatData = message.AnalogInDataFloat.Count > 0;
                var dataCount = hasFloatData ? message.AnalogInDataFloat.Count : message.AnalogInData.Count;

                for (var dataIndex = 0; dataIndex < dataCount && dataIndex < activeAnalogChannels.Count; dataIndex++)
                {
                    var channel = activeAnalogChannels[dataIndex];
                    double scaledValue;
                    if (hasFloatData)
                    {
                        // Float values are already voltage-scaled by the firmware — no calibration needed
                        scaledValue = message.AnalogInDataFloat[dataIndex];
                    }
                    else
                    {
                        // Raw ADC count — apply channel calibration/scaling via Core
                        scaledValue = channel.GetScaledValue((int)message.AnalogInData[dataIndex]);
                    }

                    var sample = new DataSample(this, channel, messageTimestamp, scaledValue);
                    channel.ActiveSample = sample;
                }

                if (dataCount != activeAnalogChannels.Count)
                {
                    AppLogger.Warning($"[CHANNEL_MAPPING] Analog data count mismatch: received {dataCount} data points for {activeAnalogChannels.Count} active channels");
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
            var analogChannelMask = BuildActiveAnalogChannelMask();
            var hasActiveDigitalChannels = DataChannels.Any(
                channel => channel.IsActive && channel.Type == ChannelType.Digital);

            SendMessage(hasActiveDigitalChannels
                ? ScpiMessageProducer.EnableDioPorts()
                : ScpiMessageProducer.DisableDioPorts());

            var coreDevice = GetCoreDeviceForSd();
            coreDevice.StreamingFrequency = StreamingFrequency;

            // The Core package resumes StartSdCardLoggingAsync continuations on the caller's
            // synchronization context. Running it on the thread pool prevents UI deadlocks.
            Task.Run(() => coreDevice.StartSdCardLoggingAsync(channelMask: analogChannelMask)).GetAwaiter().GetResult();

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

        var coreDevice = GetCoreDeviceForSd();
        var files = Task.Run(() => coreDevice.GetSdCardFilesAsync()).GetAwaiter().GetResult();
        UpdateSdCardFiles(MapSdCardFiles(files));
    }

    private string BuildActiveAnalogChannelMask()
    {
        var channelSetByte = 0u;

        foreach (var channel in DataChannels.Where(c => c.IsActive && c.Type == ChannelType.Analog))
        {
            channelSetByte |= 1u << channel.Index;
        }

        return Convert.ToString((long)channelSetByte, 2);
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

    private CoreStreamingDevice GetCoreDeviceForNetworkConfiguration()
    {
        var coreDevice = CoreDeviceForNetworkConfiguration;
        if (coreDevice == null)
        {
            throw new InvalidOperationException("Device is not connected.");
        }

        return coreDevice;
    }

    private static List<SdCardFile> MapSdCardFiles(IEnumerable<CoreSdCardFileInfo> files)
    {
        return files.Select(file => new SdCardFile
            {
                FileName = file.FileName,
                CreatedDate = file.CreatedDate ?? DateTime.MinValue
            })
            .Where(file => !string.IsNullOrWhiteSpace(file.FileName))
            .ToList();
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

        SendMessage(ScpiMessageProducer.StartStreaming(StreamingFrequency));
        IsStreaming = true;
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
        SendMessage(ScpiMessageProducer.StopStreaming);

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
    /// Handles the <see cref="DaqifiDevice.ChannelsPopulated"/> event from Core.
    /// Syncs metadata and channel wrappers from the already-populated Core device.
    /// </summary>
    protected void OnCoreChannelsPopulated(object? sender, ChannelsPopulatedEventArgs e)
    {
        if (sender is not DaqifiDevice coreDevice)
        {
            return;
        }

        SyncFromCoreDevice(coreDevice);
    }

    /// <summary>
    /// Applies the current Core device metadata and channel state to the desktop wrapper.
    /// Core remains the source of truth for status parsing and channel creation; desktop keeps
    /// its richer channel objects for WPF-only concerns.
    /// </summary>
    /// <param name="coreDevice">The already-populated Core device.</param>
    protected void SyncFromCoreDevice(DaqifiDevice coreDevice)
    {
        ArgumentNullException.ThrowIfNull(coreDevice);

        try
        {
            HydrateDeviceMetadata(coreDevice.Metadata);
            SyncChannelsFromCore(coreDevice.Channels);
            DeviceState = coreDevice.State;

            AppLogger.Information(
                $"Synchronized {coreDevice.Channels.Count} channels from Core " +
                $"({coreDevice.Channels.Count(c => c.Type == ChannelType.Analog)} analog, " +
                $"{coreDevice.Channels.Count(c => c.Type == ChannelType.Digital)} digital)");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"Failed to synchronize Core device state for {DisplayIdentifier}");
            throw;
        }
    }

    private void SyncChannelsFromCore(IReadOnlyList<Daqifi.Core.Channel.IChannel> coreChannels)
    {
        var existingChannels = DataChannels.ToDictionary(GetChannelKey);
        var updatedChannels = new List<IChannel>(coreChannels.Count);

        foreach (var coreChannel in coreChannels)
        {
            var channelKey = GetChannelKey(coreChannel);

            if (existingChannels.TryGetValue(channelKey, out var existingChannel))
            {
                switch (existingChannel)
                {
                    case AnalogChannel desktopAnalogChannel
                        when coreChannel is Daqifi.Core.Channel.IAnalogChannel coreAnalogChannel:
                        desktopAnalogChannel.ReplaceCoreChannel(coreAnalogChannel);
                        desktopAnalogChannel.DeviceName = DevicePartNumber;
                        desktopAnalogChannel.DeviceSerialNo = DeviceSerialNo;
                        updatedChannels.Add(desktopAnalogChannel);
                        continue;

                    case DigitalChannel desktopDigitalChannel
                        when coreChannel is Daqifi.Core.Channel.IDigitalChannel coreDigitalChannel:
                        desktopDigitalChannel.ReplaceCoreChannel(coreDigitalChannel);
                        desktopDigitalChannel.DeviceName = DevicePartNumber;
                        desktopDigitalChannel.DeviceSerialNo = DeviceSerialNo;
                        updatedChannels.Add(desktopDigitalChannel);
                        continue;
                }
            }

            var desktopChannel = CreateDesktopChannel(coreChannel);
            if (desktopChannel != null)
            {
                updatedChannels.Add(desktopChannel);
            }
        }

        DataChannels.Clear();
        DataChannels.AddRange(updatedChannels);
    }

    private IChannel? CreateDesktopChannel(Daqifi.Core.Channel.IChannel coreChannel)
    {
        return coreChannel switch
        {
            Daqifi.Core.Channel.IAnalogChannel coreAnalogChannel => new AnalogChannel(this, coreAnalogChannel),
            Daqifi.Core.Channel.IDigitalChannel coreDigitalChannel => new DigitalChannel(this, coreDigitalChannel),
            _ => null
        };
    }

    private static string GetChannelKey(IChannel channel)
    {
        return $"{channel.Type}:{channel.Index}";
    }

    private static string GetChannelKey(Daqifi.Core.Channel.IChannel channel)
    {
        return $"{channel.Type}:{channel.ChannelNumber}";
    }

    private void HydrateDeviceMetadata(DeviceMetadata coreMetadata)
    {
        ArgumentNullException.ThrowIfNull(coreMetadata);

        Metadata.PartNumber = coreMetadata.PartNumber;
        Metadata.SerialNumber = coreMetadata.SerialNumber;
        Metadata.FirmwareVersion = coreMetadata.FirmwareVersion;
        Metadata.HardwareRevision = coreMetadata.HardwareRevision;
        Metadata.DeviceType = coreMetadata.DeviceType;
        Metadata.Capabilities = CloneCapabilities(coreMetadata.Capabilities);
        Metadata.IpAddress = coreMetadata.IpAddress;
        Metadata.MacAddress = coreMetadata.MacAddress;
        Metadata.Ssid = coreMetadata.Ssid;
        Metadata.HostName = coreMetadata.HostName;
        Metadata.DevicePort = coreMetadata.DevicePort;
        Metadata.WifiSecurityMode = coreMetadata.WifiSecurityMode;
        Metadata.WifiInfrastructureMode = coreMetadata.WifiInfrastructureMode;

        // DeviceType is an [ObservableProperty] and not backed by Metadata, so set it explicitly.
        DeviceType = Metadata.DeviceType;

        // All other identity properties delegate directly to Metadata — just notify bindings.
        OnPropertyChanged(nameof(DevicePartNumber));
        OnPropertyChanged(nameof(DeviceSerialNo));
        OnPropertyChanged(nameof(DeviceVersion));
        OnPropertyChanged(nameof(IpAddress));
        OnPropertyChanged(nameof(MacAddress));
        OnPropertyChanged(nameof(DisplayIdentifier));

        if (!string.IsNullOrWhiteSpace(Metadata.Ssid))
        {
            NetworkConfiguration.Ssid = Metadata.Ssid;
        }

        // Security type 0 is the open-network case and should not be filtered out.
        NetworkConfiguration.SecurityType = (WifiSecurityType)Metadata.WifiSecurityMode;

        if (Metadata.WifiInfrastructureMode > 0)
        {
            NetworkConfiguration.Mode = (WifiMode)Metadata.WifiInfrastructureMode;
        }

        AppLogger.Information($"Detected device type: {DeviceType} from part number: {Metadata.PartNumber}");
    }

    private static DeviceCapabilities CloneCapabilities(DeviceCapabilities capabilities)
    {
        return new DeviceCapabilities
        {
            SupportsStreaming = capabilities.SupportsStreaming,
            HasSdCard = capabilities.HasSdCard,
            HasWiFi = capabilities.HasWiFi,
            HasUsb = capabilities.HasUsb,
            AnalogInputChannels = capabilities.AnalogInputChannels,
            AnalogOutputChannels = capabilities.AnalogOutputChannels,
            DigitalChannels = capabilities.DigitalChannels,
            MaxSamplingRate = capabilities.MaxSamplingRate
        };
    }

    #endregion

    public void InitializeDeviceState()
    {
        // Initialize protocol handler for automatic message routing
        InitializeProtocolHandler();
    }

    public async Task UpdateNetworkConfiguration()
    {
        var coreDevice = GetCoreDeviceForNetworkConfiguration();

        var restoreSdInterface = ConnectionType == ConnectionType.Usb && Mode == DeviceMode.LogToDevice;

        if (IsStreaming)
        {
            StopStreaming();
        }

        try
        {
            await coreDevice.UpdateNetworkConfigurationAsync(NetworkConfiguration);
        }
        finally
        {
            // Core always restores LAN after applying settings. USB devices that are currently
            // logging to the SD card need the desktop wrapper to switch the shared SPI bus back,
            // even if the Core update fails after toggling the shared SPI bus to LAN.
            if (restoreSdInterface)
            {
                PrepareSdInterface();
            }
        }
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

        if (ConnectionType == ConnectionType.Usb)
        {
            SendMessage(ScpiMessageProducer.SetStreamInterface(StreamInterface.SdCard));
        }
    }

    // SD and LAN can't both be enabled due to hardware limitations
    private void PrepareLanInterface()
    {
        SendMessage(ScpiMessageProducer.DisableStorageSd);
        SendMessage(ScpiMessageProducer.EnableNetworkLan);

        if (ConnectionType == ConnectionType.Usb)
        {
            SendMessage(ScpiMessageProducer.SetStreamInterface(StreamInterface.Usb));
        }
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
