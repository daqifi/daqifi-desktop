using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Core.Communication;
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

    // DeviceType property with default value of Unknown.
    // HasWincWifiModule is derived from DeviceType, so notify it when DeviceType changes.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWincWifiModule))]
    private DeviceType _deviceType = DeviceType.Unknown;

    // DeviceState property for tracking device state
    [ObservableProperty]
    private DeviceState _deviceState = DeviceState.Disconnected;

    /// <summary>
    /// Detection window for leftover frames at stream start, in seconds of device-counter time.
    /// The device holds the final frame of a stopped session in its transmit path and emits it
    /// as the first frame of the next session (issue #573); that frame's counter sits one sample
    /// period (1 s at the minimum 1 Hz rate) after the last counter seen before the stop, while
    /// a genuine new-session frame is offset by the full stop-to-start gap. The same window also
    /// bounds the plausible counter advance between two consecutive frames of one session, which
    /// is how the no-reference first-frame validation tells a leftover from genuine data.
    /// </summary>
    private const double STALE_FRAME_WINDOW_SECONDS = 2.5;

    /// <summary>
    /// Safety cap on leftover-frame discards per stream start so genuine data can never be
    /// dropped indefinitely (e.g., a stop-to-start gap that aliases the ~86 s counter wrap).
    /// </summary>
    private const int MAX_DISCARDED_LEFTOVER_FRAMES = 5;

    /// <summary>
    /// Fallback device counter frequency in Hz when the device has not reported one.
    /// Matches the 20 ns tick period assumed by Core's <see cref="TimestampProcessor"/>.
    /// </summary>
    private const uint DEFAULT_TIMESTAMP_FREQUENCY = 50_000_000;

    private const string SD_UNAVAILABLE_MESSAGE = "Core SD card operations are not available for this device.";
    private const string STREAMING_UNAVAILABLE_MESSAGE = "Core live streaming operations are not available for this device.";
    private const string NOT_CONNECTED_MESSAGE = "Device is not connected.";

    private readonly ITimestampProcessor _timestampProcessor = new TimestampProcessor();
    private List<SdCardFile> _sdCardFiles = [];

    // Leftover-frame detection state (issue #573). The last-seen counter is tracked for every
    // stream frame — including frames dropped while not streaming — because the leftover frame
    // emitted at the next start follows the device's last emitted frame by one sample period.
    private uint _lastSeenDeviceTimestamp;
    private bool _hasSeenDeviceTimestamp;
    private volatile bool _checkForLeftoverFrames;
    private int _discardedLeftoverFrameCount;

    // First-frame validation state (issue #573 follow-up). The device's leftover frame survives
    // a USB disconnect/reconnect, and a freshly connected instance has no counter reference to
    // recognize it against — so the first frame of the first session is held until the next
    // frame's counter delta validates the pair as same-session data.
    private volatile bool _pendingFirstFrameValidation;
    private DaqifiOutMessage? _heldFirstFrame;

    // Protocol handler for automatic message routing
    private IProtocolHandler? _protocolHandler;

    /// <summary>
    /// The Core streaming device created by the shared <see cref="Connect"/> template,
    /// or null while disconnected.
    /// </summary>
    protected CoreStreamingDevice? CoreDevice { get; set; }

    /// <summary>
    /// Core streaming device used for SD card operations (USB devices only).
    /// </summary>
    protected virtual CoreStreamingDevice? CoreDeviceForSd => null;

    /// <summary>
    /// Core streaming device used for live stream start/stop operations.
    /// </summary>
    protected virtual CoreStreamingDevice? CoreDeviceForStreaming => CoreDevice;

    /// <summary>
    /// Core streaming device used for network configuration orchestration.
    /// </summary>
    protected virtual CoreStreamingDevice? CoreDeviceForNetworkConfiguration => CoreDevice;

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

    public SdCardLogFormat SdCardLogFormat { get; set; } = SdCardLogFormat.Protobuf;

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
    /// Gets the best available human-readable name for this device.
    /// Returns the serial number when populated, otherwise falls back to DisplayIdentifier.
    /// </summary>
    public string DeviceDisplayName =>
        !string.IsNullOrWhiteSpace(DeviceSerialNo) ? DeviceSerialNo : DisplayIdentifier;

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
    /// Gets whether the device is currently connected via its Core device.
    /// </summary>
    public virtual bool IsConnected => CoreDevice?.IsConnected == true;

    public bool IsStreaming { get; set; }
    public bool IsFirmwareOutdated { get; set; }

    /// <summary>
    /// True only for the Nyquist family, which carries a separately-flashable WINC1500 WiFi
    /// module. ESP32-based and unrecognized devices (DeviceType.Unknown) integrate WiFi into the
    /// SoC and expose no WINC firmware version — for those, <c>SYSTem:COMMunicate:LAN:GETChipInfo?</c>
    /// returns non-version data (e.g. the IP address), so the WiFi-firmware check must not run.
    /// </summary>
    public bool HasWincWifiModule =>
        DeviceType is DeviceType.Nyquist1 or DeviceType.Nyquist2 or DeviceType.Nyquist3;

    /// <summary>
    /// Gets or sets whether the device's WiFi module firmware needs flashing (below the minimum
    /// supported version or unreadable). Only meaningful when <see cref="HasWincWifiModule"/> is true.
    /// </summary>
    [ObservableProperty]
    private bool _isWifiFirmwareOutdated;

    /// <summary>
    /// Gets or sets the WiFi module firmware version reported by the device, or <c>"Unknown"</c>
    /// when the chip-info query could not be completed.
    /// </summary>
    [ObservableProperty]
    private string _wifiFirmwareVersion = "Unknown";

    /// <summary>
    /// Gets the device's hardware timestamp clock frequency in Hz.
    /// Sourced from the underlying Core device, which populates it from the
    /// <c>TimestampFreq</c> field in received status messages.
    /// Used as a fallback when parsing SD card files that lack this field.
    /// </summary>
    public uint TimestampFrequency => (CoreDeviceForSd ?? CoreDeviceForStreaming)?.TimestampFrequency ?? 0;

    // Debug mode properties
    public bool IsDebugModeEnabled { get; private set; }
    public event Action<DebugDataModel>? DebugDataReceived;
    #endregion

    #region Abstract Methods
    public abstract bool Write(string command);

    /// <summary>
    /// Sends a message to the device. Must be implemented in derived classes.
    /// Core-based devices use DaqifiDevice.Send() for sending messages.
    /// </summary>
    /// <param name="message">The SCPI message to send.</param>
    protected abstract void SendMessage(IOutboundMessage<string> message);
    #endregion

    #region Core Connection Template
    /// <summary>
    /// Connects to the device using the shared Core connect/wire/initialize skeleton:
    /// tear down any previous connection, create the transport-specific Core device
    /// (<see cref="CreateCoreDevice"/>), subscribe Core events, initialize desktop state, run
    /// Core's async initialization, then run the optional transport-specific post-initialize
    /// step (<see cref="OnCoreDeviceInitialized"/>). On any failure the attempt is logged
    /// (<see cref="LogConnectFailure"/>) and fully cleaned up (<see cref="CleanupConnection"/>).
    /// </summary>
    /// <remarks>
    /// Synchronous by contract: <see cref="ConnectionManager"/> invokes <c>Connect()</c> from
    /// <c>Task.Run</c>, which is what makes blocking on <c>InitializeAsync()</c> safe here.
    /// Do not call this from the UI thread.
    /// </remarks>
    /// <returns><c>true</c> when the device connected and initialized; otherwise <c>false</c>.</returns>
    public virtual bool Connect()
    {
        // Ensure any previous connection state is cleaned up first
        CleanupConnection();

        try
        {
            var coreDevice = CreateCoreDevice();
            if (coreDevice == null)
            {
                return false;
            }

            CoreDevice = coreDevice;

            coreDevice.ChannelsPopulated += OnCoreChannelsPopulated;
            coreDevice.MessageReceived += OnCoreMessageReceived;

            InitializeDeviceState();

            // Blocking on Core's async initialization is safe because Connect() is invoked
            // from Task.Run by ConnectionManager (see remarks).
            coreDevice.InitializeAsync().GetAwaiter().GetResult();

            OnCoreDeviceInitialized();
            return true;
        }
        catch (Exception ex)
        {
            LogConnectFailure(ex);
            CleanupConnection();
            return false;
        }
    }

    /// <summary>
    /// Disconnects from the device: stops streaming, unsubscribes Core events, clears
    /// channels, and tears down the connection via <see cref="CleanupConnection"/>.
    /// </summary>
    /// <returns><c>true</c> when the disconnect completed; otherwise <c>false</c>.</returns>
    public virtual bool Disconnect()
    {
        try
        {
            StopStreaming();
        }
        catch (Exception ex)
        {
            // A dead transport (e.g., USB unplugged mid-stream) must not abort teardown.
            AppLogger.Warning(ex, $"Error stopping streaming while disconnecting DAQiFi device {DisplayIdentifier}");
        }

        try
        {
            // Unsubscribe before clearing channels so a late ChannelsPopulated event
            // cannot repopulate the list after the clear.
            if (CoreDevice != null)
            {
                UnsubscribeCoreDeviceEvents(CoreDevice);
            }

            // Clear channels to prevent ghost channels on reconnect (Issue #29)
            AppLogger.Information($"Cleared {DataChannels.Count} channels for device {DeviceSerialNo}");
            DataChannels.Clear();

            CleanupConnection();
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"Error disconnecting from DAQiFi device {DisplayIdentifier}");
            return false;
        }
    }

    /// <summary>
    /// Creates and connects the transport-specific Core streaming device for the shared
    /// <see cref="Connect"/> template. Return <c>null</c> (after logging) to fail without an
    /// exception, or throw to route the failure through <see cref="LogConnectFailure"/>.
    /// Intermediate state assigned before a throw (e.g., a transport, or
    /// <see cref="CoreDevice"/> itself) is torn down by <see cref="CleanupConnection"/>.
    /// The default returns <c>null</c>; devices using the shared template must override.
    /// </summary>
    protected virtual CoreStreamingDevice? CreateCoreDevice() => null;

    /// <summary>
    /// Called by <see cref="Connect"/> after Core's <c>InitializeAsync()</c> completes.
    /// Default does nothing; serial overrides to block until the device reports its
    /// initial status message.
    /// </summary>
    protected virtual void OnCoreDeviceInitialized()
    {
    }

    /// <summary>
    /// Logs a failure of the shared <see cref="Connect"/> template. Default logs an error;
    /// transports override to add context or downgrade user/environmental conditions
    /// (e.g., a COM port that disappeared) to warnings.
    /// </summary>
    /// <param name="ex">The exception that failed the connection attempt.</param>
    protected virtual void LogConnectFailure(Exception ex)
    {
        AppLogger.Error(ex, $"Problem connecting to DAQiFi device {DisplayIdentifier}");
    }

    /// <summary>
    /// Tears down the Core device created by <see cref="Connect"/>: unsubscribes Core
    /// events, disconnects, and disposes. Safe to call when no Core device is set.
    /// Transports override to also tear down transport-specific state and must call the
    /// base implementation.
    /// </summary>
    protected virtual void CleanupConnection()
    {
        var coreDevice = CoreDevice;
        if (coreDevice == null)
        {
            return;
        }

        UnsubscribeCoreDeviceEvents(coreDevice);

        try
        {
            coreDevice.Disconnect();
        }
        catch (Exception ex)
        {
            AppLogger.Warning(ex, "Error disconnecting Core device during cleanup");
        }

        try
        {
            coreDevice.Dispose();
        }
        catch (Exception ex)
        {
            AppLogger.Warning(ex, "Error disposing Core device during cleanup");
        }

        CoreDevice = null;
    }

    private void UnsubscribeCoreDeviceEvents(CoreStreamingDevice coreDevice)
    {
        coreDevice.ChannelsPopulated -= OnCoreChannelsPopulated;
        coreDevice.MessageReceived -= OnCoreMessageReceived;
    }

    /// <summary>
    /// Handles non-status messages received from Core's DaqifiDevice and routes them
    /// to the protocol handler for streaming data processing.
    /// Status messages are handled via <see cref="OnCoreChannelsPopulated"/>.
    /// </summary>
    private void OnCoreMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        var args = new MessageEventArgs<object>(e.Message);
        HandleInboundMessage(args);
    }
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
            // Track the counter even while not streaming: the device can emit a final frame
            // after the stop command lands, and the leftover frame at the next start follows
            // it by one sample period — it must be included in the detection reference.
            TrackLastSeenDeviceTimestamp(message.MsgTimeStamp);
            return;
        }

        if (_checkForLeftoverFrames)
        {
            if (IsLeftoverFrameFromPreviousSession(message.MsgTimeStamp))
            {
                TrackLastSeenDeviceTimestamp(message.MsgTimeStamp);
                return;
            }

            _checkForLeftoverFrames = false;
        }

        if (_pendingFirstFrameValidation)
        {
            ValidateFirstFramesWithoutReference(message);
            return;
        }

        ProcessStreamMessage(message);
    }

    /// <summary>
    /// Processes a validated streaming frame: computes its timestamp, updates channel samples,
    /// and dispatches the device message to the logging pipeline.
    /// </summary>
    /// <param name="message">The streaming protobuf message to process.</param>
    private void ProcessStreamMessage(DaqifiOutMessage message)
    {
        TrackLastSeenDeviceTimestamp(message.MsgTimeStamp);

        // Protocol handler already validated this is a streaming message with timestamp
        // No need to revalidate here

        var deviceId = message.DeviceSn.ToString(CultureInfo.InvariantCulture);

        // Use Core's TimestampProcessor for rollover handling
        var timestampResult = _timestampProcessor.ProcessTimestamp(deviceId, message.MsgTimeStamp);
        var messageTimestamp = timestampResult.Timestamp;
        var rollover = timestampResult.WasRollover;
        // Firmware-measured inter-message delta (immune to TCP jitter); null for the first message.
        var firmwareDeltaMs = timestampResult.IsFirstMessage
            ? (double?)null
            : timestampResult.SecondsBetweenMessages * 1000.0;

        var digitalCount = 0;
        var analogCount = 0;

        var hasDigitalData = message.DigitalData.Length > 0;
        // USB firmware sends pre-scaled floats (AnalogInDataFloat); WiFi sends raw ADC counts (AnalogInData).
        var hasAnalogData = message.AnalogInData.Count > 0 || message.AnalogInDataFloat.Count > 0;

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

                    var sample = new DataSample(this, channel, messageTimestamp, scaledValue, firmwareDeltaMs);
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

        // Process digital channels. The firmware streams the whole DIO port as a raw pin-state
        // snapshot — bit N is pin N regardless of which channels are enabled, because the
        // wire-level DIO enable is port-global, not per pin — so bits are indexed by channel
        // number, not by position in the active-channel list (issue #663).
        if (hasDigitalData)
        {
            try
            {
                var activeDigitalChannels = DataChannels.Where(c => c.IsActive && c.Type == ChannelType.Digital)
                                                       .OrderBy(c => c.Index)
                                                       .ToList();

                foreach (var channel in activeDigitalChannels)
                {
                    // Output-direction channels display the commanded state, not streamed data
                    if (channel.Direction != ChannelDirection.Input)
                    {
                        continue;
                    }

                    var byteIndex = channel.Index / 8;
                    if (channel.Index < 0 || byteIndex >= message.DigitalData.Length)
                    {
                        continue; // pin beyond the streamed payload: no sample
                    }

                    var bit = (message.DigitalData[byteIndex] & (1 << (channel.Index % 8))) != 0;
                    channel.ActiveSample = new DataSample(
                        this, channel, messageTimestamp, Convert.ToInt32(bit), firmwareDeltaMs);
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

        DispatchDeviceMessage(deviceMessage);
    }

    /// <summary>
    /// Forwards a parsed streaming frame's device message to the logging pipeline.
    /// Virtual so tests can intercept the dispatch without the application service provider.
    /// </summary>
    /// <param name="deviceMessage">The device message built from the streaming frame.</param>
    protected virtual void DispatchDeviceMessage(DeviceMessage deviceMessage)
    {
        Logger.LoggingManager.Instance.HandleDeviceMessage(this, deviceMessage);
    }

    /// <summary>
    /// Records the most recent device counter value seen on any stream frame.
    /// </summary>
    /// <param name="deviceTimestamp">The raw 32-bit counter value from the frame.</param>
    private void TrackLastSeenDeviceTimestamp(uint deviceTimestamp)
    {
        _lastSeenDeviceTimestamp = deviceTimestamp;
        _hasSeenDeviceTimestamp = true;
    }

    /// <summary>
    /// Determines whether a frame received at stream start is a leftover from the previous
    /// streaming session (issue #573). The device's free-running counter is never reset, so a
    /// leftover frame sits within one sample period of the last counter value seen before the
    /// stop, while a genuine new-session frame is offset by the full stop-to-start gap. Modular
    /// uint subtraction keeps the comparison correct across the ~86 s counter wrap.
    /// </summary>
    /// <param name="deviceTimestamp">The raw 32-bit counter value from the frame.</param>
    /// <returns><c>true</c> if the frame belongs to the previous session and must be discarded.</returns>
    private bool IsLeftoverFrameFromPreviousSession(uint deviceTimestamp)
    {
        if (!_hasSeenDeviceTimestamp || _discardedLeftoverFrameCount >= MAX_DISCARDED_LEFTOVER_FRAMES)
        {
            return false;
        }

        var timestampFrequency = TimestampFrequency != 0 ? TimestampFrequency : DEFAULT_TIMESTAMP_FREQUENCY;
        var elapsedSeconds = unchecked(deviceTimestamp - _lastSeenDeviceTimestamp) / (double)timestampFrequency;
        if (elapsedSeconds >= STALE_FRAME_WINDOW_SECONDS)
        {
            return false;
        }

        _discardedLeftoverFrameCount++;
        AppLogger.Information(
            $"Discarded leftover frame from previous streaming session at stream start " +
            $"(counter advanced {elapsedSeconds:F4}s, discard {_discardedLeftoverFrameCount} of {MAX_DISCARDED_LEFTOVER_FRAMES} max)");
        return true;
    }

    /// <summary>
    /// Validates the leading frames of a session started without a counter reference (the first
    /// session after connect). The device's leftover frame survives a USB disconnect/reconnect,
    /// so the first frame cannot be trusted on arrival: it is held until the next frame arrives,
    /// and released only when the pair's counter delta fits within one session
    /// (&lt; <see cref="STALE_FRAME_WINDOW_SECONDS"/>). A held frame whose successor implies a
    /// multi-second jump is a leftover from an earlier session and is discarded, the successor
    /// becoming the new held candidate — capped at <see cref="MAX_DISCARDED_LEFTOVER_FRAMES"/>
    /// so genuine data can never be withheld indefinitely. Costs one sample period of latency
    /// on the first sample of the first session only.
    /// </summary>
    /// <param name="message">The streaming frame received while validation is pending.</param>
    private void ValidateFirstFramesWithoutReference(DaqifiOutMessage message)
    {
        var heldFrame = _heldFirstFrame;
        if (heldFrame == null)
        {
            _heldFirstFrame = message;
            TrackLastSeenDeviceTimestamp(message.MsgTimeStamp);
            return;
        }

        var timestampFrequency = TimestampFrequency != 0 ? TimestampFrequency : DEFAULT_TIMESTAMP_FREQUENCY;
        var elapsedSeconds = unchecked(message.MsgTimeStamp - heldFrame.MsgTimeStamp) / (double)timestampFrequency;
        if (elapsedSeconds >= STALE_FRAME_WINDOW_SECONDS
            && _discardedLeftoverFrameCount < MAX_DISCARDED_LEFTOVER_FRAMES)
        {
            _discardedLeftoverFrameCount++;
            AppLogger.Information(
                $"Discarded leftover frame at stream start (no counter reference; counter advanced " +
                $"{elapsedSeconds:F4}s to the next frame, discard {_discardedLeftoverFrameCount} of " +
                $"{MAX_DISCARDED_LEFTOVER_FRAMES} max)");
            _heldFirstFrame = message;
            TrackLastSeenDeviceTimestamp(message.MsgTimeStamp);
            return;
        }

        _pendingFirstFrameValidation = false;
        _heldFirstFrame = null;
        ProcessStreamMessage(heldFrame);
        ProcessStreamMessage(message);
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
                // Core's StartSdCardLoggingAsync handles the SD interface setup
                // (DisableNetworkLan, EnableStorageSd, SetStreamInterface).
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
            // Build channel bitmask — Core's StartSdCardLoggingAsync sends EnableAdcChannels
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

            // Core doesn't handle DIO enable yet, so desktop still sends this directly
            SendMessage(hasDigitalChannels
                ? ScpiMessageProducer.EnableDioPorts()
                : ScpiMessageProducer.DisableDioPorts());

            var coreDevice = GetCoreDevice(CoreDeviceForSd, SD_UNAVAILABLE_MESSAGE);
            coreDevice.StreamingFrequency = StreamingFrequency;

            // The Core package resumes StartSdCardLoggingAsync continuations on the caller's
            // synchronization context. Running it on the thread pool prevents UI deadlocks.
            var channelMaskString = analogChannelMask.ToString(CultureInfo.InvariantCulture);
            Task.Run(() => coreDevice.StartSdCardLoggingAsync(channelMask: channelMaskString, format: SdCardLogFormat)).GetAwaiter().GetResult();

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
            var coreDevice = GetCoreDevice(CoreDeviceForSd, SD_UNAVAILABLE_MESSAGE);
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

    /// <summary>
    /// Refreshes the list of SD card log files from the connected USB device.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the device is not connected via USB or SD operations cannot be performed.</exception>
    public void RefreshSdCardFiles()
    {
        if (ConnectionType != ConnectionType.Usb)
        {
            throw new InvalidOperationException("SD Card access is only available when connected via USB");
        }

        EnsureSdOperationsQuiesced();

        var coreDevice = GetCoreDevice(CoreDeviceForSd, SD_UNAVAILABLE_MESSAGE);
        var files = Task.Run(() => coreDevice.GetSdCardFilesAsync()).GetAwaiter().GetResult();
        UpdateSdCardFiles(MapSdCardFiles(files));
    }

    /// <summary>
    /// Replaces the current SD card file list and raises a property change notification.
    /// </summary>
    public void UpdateSdCardFiles(List<SdCardFile> files)
    {
        _sdCardFiles = files;
        OnPropertyChanged(nameof(SdCardFiles));
    }

    /// <summary>
    /// Returns the given Core device, or throws <see cref="InvalidOperationException"/> with
    /// <paramref name="unavailableMessage"/> when the operation is not available.
    /// </summary>
    private static CoreStreamingDevice GetCoreDevice(CoreStreamingDevice? coreDevice, string unavailableMessage)
    {
        return coreDevice ?? throw new InvalidOperationException(unavailableMessage);
    }

    /// <summary>
    /// Downloads an SD card log file from the connected device to a local temp path.
    /// </summary>
    /// <param name="fileName">The name of the file on the SD card.</param>
    /// <param name="progress">Optional progress reporter for transfer updates.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when streaming or SD logging is active.</exception>
    public async Task<SdCardDownloadResult> DownloadSdCardFileAsync(
        string fileName,
        IProgress<SdCardTransferProgress>? progress = null,
        CancellationToken ct = default)
    {
        EnsureSdOperationsQuiesced();

        var coreDevice = GetCoreDevice(CoreDeviceForSd, SD_UNAVAILABLE_MESSAGE);
        return await coreDevice.DownloadSdCardFileAsync(fileName, progress, ct);
    }

    /// <summary>
    /// Deletes an SD card log file from the connected device.
    /// </summary>
    /// <param name="fileName">The name of the file to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when streaming or SD logging is active.</exception>
    public async Task DeleteSdCardFileAsync(string fileName, CancellationToken ct = default)
    {
        EnsureSdOperationsQuiesced();

        var coreDevice = GetCoreDevice(CoreDeviceForSd, SD_UNAVAILABLE_MESSAGE);
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
            throw new InvalidOperationException(
                "Cannot perform SD card file operations while logging to the SD card. Stop logging first.");
        }

        if (IsStreaming)
        {
            throw new InvalidOperationException(
                "Cannot perform SD card file operations while streaming. Stop streaming first.");
        }
    }

    private static bool IsImportableSdLogFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var extension = Path.GetExtension(fileName);
        return extension.Equals(".bin", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".csv", StringComparison.OrdinalIgnoreCase);
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

        var coreStreamingDevice = GetCoreDevice(CoreDeviceForStreaming, STREAMING_UNAVAILABLE_MESSAGE);
        coreStreamingDevice.StreamingFrequency = StreamingFrequency;

        // A session must never anchor its time axis on prior-session data (issue #573).
        // Reset the timestamp baseline here — StopStreaming's reset is skipped on unplug and
        // error paths — and arm leftover-frame detection: the device holds the final frame of
        // the previous session in its transmit path and emits it first when streaming resumes.
        // With a counter reference (any frame seen on this connection), leftovers are recognized
        // directly; without one (first session after connect — the device's leftover survives a
        // disconnect/reconnect), the first frame is held and validated against its successor.
        _timestampProcessor.ResetAll();
        _discardedLeftoverFrameCount = 0;
        _checkForLeftoverFrames = _hasSeenDeviceTimestamp;
        _pendingFirstFrameValidation = !_hasSeenDeviceTimestamp;
        _heldFirstFrame = null;

        coreStreamingDevice.StartStreaming();
        IsStreaming = coreStreamingDevice.IsStreaming;
        AppLogger.AddBreadcrumb("streaming", $"Streaming started at {StreamingFrequency} Hz");
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

        var coreStreamingDevice = GetCoreDevice(CoreDeviceForStreaming, STREAMING_UNAVAILABLE_MESSAGE);
        coreStreamingDevice.StopStreaming();
        IsStreaming = coreStreamingDevice.IsStreaming;
        AppLogger.AddBreadcrumb("streaming", "Streaming stopped");

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

    /// <summary>
    /// Enables multiple channels on the device with a single SCPI command per channel type.
    /// </summary>
    public void AddChannels(IEnumerable<IChannel> channelsToAdd)
    {
        var channelSetByte = 0u;
        var hasAnalog = false;
        var hasDigital = false;

        foreach (var channelToAdd in channelsToAdd)
        {
            var channel = DataChannels.FirstOrDefault(c => Equals(c, channelToAdd));
            if (channel == null) continue;

            switch (channel.Type)
            {
                case ChannelType.Analog:
                    channelSetByte |= (1u << channel.Index);
                    channel.IsActive = true;
                    hasAnalog = true;
                    break;
                case ChannelType.Digital:
                    channel.IsActive = true;
                    hasDigital = true;
                    break;
            }
        }

        if (hasAnalog)
        {
            SendMessage(ScpiMessageProducer.EnableAdcChannels(Convert.ToString(channelSetByte)));
        }
        if (hasDigital)
        {
            SendMessage(ScpiMessageProducer.EnableDioPorts());
        }
    }

    /// <summary>
    /// Disables all channels on the device.
    /// </summary>
    public void RemoveAllChannels()
    {
        SendMessage(ScpiMessageProducer.EnableAdcChannels("0"));
        SendMessage(ScpiMessageProducer.DisableDioPorts());
        foreach (var channel in DataChannels)
        {
            channel.IsActive = false;
        }
    }

    private IEnumerable<IChannel> GetActiveAnalogChannels()
    {
        return DataChannels.Where(channel => channel.Type == ChannelType.Analog && channel.IsActive).ToList();
    }

    /// <inheritdoc />
    public void SetChannelOutputValue(IChannel channel, double value)
    {
        if (channel.Type != ChannelType.Digital)
        {
            return;
        }

        ExecuteDioCommand(channel, "drive output",
            (coreDevice, coreChannel) => coreDevice.SetDioValue(coreChannel, value >= 0.5));
    }

    /// <inheritdoc />
    public void SetChannelDirection(IChannel channel, ChannelDirection direction)
    {
        if (channel.Type != ChannelType.Digital)
        {
            return;
        }

        ExecuteDioCommand(channel, "set direction",
            (coreDevice, coreChannel) => coreDevice.SetDioDirection(coreChannel, direction));
    }

    /// <summary>
    /// Runs a Core DIO command against the wrapped Core channel. Core throws when the device
    /// is disconnected, but these calls originate from UI property setters that can race a
    /// disconnect — so unavailability is logged and swallowed to preserve the pre-delegation
    /// no-op semantics instead of surfacing an exception through a WPF binding.
    /// </summary>
    private void ExecuteDioCommand(
        IChannel channel,
        string operation,
        Action<CoreStreamingDevice, Daqifi.Core.Channel.IChannel> command)
    {
        if (channel is not DigitalChannel digitalChannel)
        {
            AppLogger.Warning($"Ignored DIO {operation} for {channel.Name}: not a digital channel wrapper.");
            return;
        }

        var coreDevice = CoreDevice;
        if (coreDevice == null || !coreDevice.IsConnected)
        {
            AppLogger.Warning($"Ignored DIO {operation} for {channel.Name}: device is not connected.");
            return;
        }

        try
        {
            command(coreDevice, digitalChannel.CoreChannel);
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"Failed to {operation} for digital channel {channel.Name}");
        }
    }

    /// <summary>
    /// Handles the <see cref="DaqifiDevice.ChannelsPopulated"/> event from Core.
    /// Syncs metadata and channel wrappers from the already-populated Core device.
    /// Virtual so transports can layer additional signaling (serial signals its
    /// initial-status wait) before the shared sync runs.
    /// </summary>
    protected virtual void OnCoreChannelsPopulated(object? sender, ChannelsPopulatedEventArgs e)
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
        var coreDevice = GetCoreDevice(CoreDeviceForNetworkConfiguration, NOT_CONNECTED_MESSAGE);

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
            //
            // This restore is best-effort: PrepareSdInterface delegates to the Core device and
            // throws when it is gone (e.g. a mid-update disconnect nulls CoreDevice). Inside a
            // finally that would mask the original UpdateNetworkConfigurationAsync failure, so
            // swallow-and-log here — the SPI bus is moot once the device is disconnected anyway.
            if (restoreSdInterface)
            {
                try
                {
                    PrepareSdInterface();
                }
                catch (Exception restoreException)
                {
                    AppLogger.Warning(
                        restoreException,
                        "Failed to restore the SD interface for device " +
                        $"{DeviceSerialNo} after a network configuration update.");
                }
            }
        }
    }

    public void Reboot()
    {
        SendMessage(ScpiMessageProducer.RebootDevice);
        Disconnect();
    }

    // SD and LAN share one SPI bus and can't both be enabled (hardware limitation).
    // Core owns the LAN-disable/SD-enable SCPI pair; the desktop only adds the USB-only
    // stream-interface switch (Core's base method omits it by design).
    private void PrepareSdInterface()
    {
        var coreDevice = GetCoreDevice(CoreDeviceForNetworkConfiguration, NOT_CONNECTED_MESSAGE);
        coreDevice.PrepareSdInterface();

        if (ConnectionType == ConnectionType.Usb)
        {
            SendMessage(ScpiMessageProducer.SetStreamInterface(StreamInterface.SdCard));
        }
    }

    // SD and LAN share one SPI bus and can't both be enabled (hardware limitation).
    // Core owns the SD-disable/LAN-enable SCPI pair; the desktop only adds the USB-only
    // stream-interface switch (Core's base method omits it by design).
    private void PrepareLanInterface()
    {
        var coreDevice = GetCoreDevice(CoreDeviceForNetworkConfiguration, NOT_CONNECTED_MESSAGE);
        coreDevice.PrepareLanInterface();

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
