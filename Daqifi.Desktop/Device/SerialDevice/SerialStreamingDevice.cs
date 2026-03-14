using System.IO.Ports;
using Daqifi.Core.Device;
using Daqifi.Core.Communication.Transport;
using Daqifi.Core.Communication.Messages;
using Daqifi.Core.Firmware;
using Daqifi.Desktop.IO.Messages;
using ScpiMessageProducer = Daqifi.Core.Communication.Producers.ScpiMessageProducer;
using CoreStreamingDevice = Daqifi.Core.Device.DaqifiStreamingDevice;

namespace Daqifi.Desktop.Device.SerialDevice;

public class SerialStreamingDevice : AbstractStreamingDevice, ILanChipInfoProvider
{
    private static readonly TimeSpan InitialStatusTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan InitialStatusPollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan InitialStatusRequestInterval = TimeSpan.FromSeconds(1);

    #region Properties
    private SerialPort? _port;
    private CoreStreamingDevice? _coreDevice;
    private SerialStreamTransport? _transport;
    private TaskCompletionSource<bool>? _initialStatusReceivedSource;

    public SerialPort? Port
    {
        get => _port;
        set
        {
            if (_port != value)
            {
                _port = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayIdentifier));
            }
        }
    }

    /// <summary>
    /// Gets the actual COM port name for UART communication
    /// This is needed because Name may be set to the device part number after discovery
    /// </summary>
    public string PortName => Port?.PortName ?? Name;

    public override ConnectionType ConnectionType => ConnectionType.Usb;

    /// <summary>
    /// Gets whether the device is currently connected via Core's transport.
    /// </summary>
    public override bool IsConnected => _coreDevice?.IsConnected == true;

    protected override CoreStreamingDevice? CoreDeviceForSd => _coreDevice;
    protected override CoreStreamingDevice? CoreDeviceForNetworkConfiguration => _coreDevice;
    #endregion

    #region Constructor
    public SerialStreamingDevice(string portName)
    {
        Name = portName;
        Port = new SerialPort(portName);
    }

    /// <summary>
    /// Creates a SerialStreamingDevice with device info from Core's discovery.
    /// Use this constructor when device has already been probed.
    /// </summary>
    public SerialStreamingDevice(string portName, string? deviceName, string? serialNumber, string? firmwareVersion)
    {
        Name = !string.IsNullOrWhiteSpace(deviceName) ? deviceName : portName;
        Port = new SerialPort(portName);
        Metadata.SerialNumber = serialNumber ?? string.Empty;
        Metadata.FirmwareVersion = firmwareVersion ?? string.Empty;
    }

    #endregion
    #region Override Methods
    public override bool Connect()
    {
        // Ensure any previous connection state is cleaned up first
        CleanupConnection();

        try
        {
            _initialStatusReceivedSource = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            // Use Core's transport for unified message handling (both send and receive)
            // Note: Transport manages the actual SerialPort connection internally
            _transport = new SerialStreamTransport(Port.PortName, enableDtr: true);
            _transport.Connect();

            // Create Core device with transport - this enables both sending AND receiving
            _coreDevice = new CoreStreamingDevice(
                string.IsNullOrWhiteSpace(Name) ? "DAQiFi Serial Device" : Name,
                _transport);
            _coreDevice.Connect();

            // Subscribe to Core device events
            _coreDevice.ChannelsPopulated += OnCoreChannelsPopulatedSerial;
            _coreDevice.MessageReceived += OnCoreMessageReceived;

            InitializeDeviceState();

            // Use Core's async initialization (safe because Connect() is called from Task.Run)
            _coreDevice.InitializeAsync().GetAwaiter().GetResult();
            WaitForInitialStatusMessage();
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"Failed to connect on {PortName}");
            CleanupConnection();
            return false;
        }
    }

    /// <summary>
    /// Handles the <see cref="DaqifiDevice.ChannelsPopulated"/> event from Core and
    /// signals the initial-status wait so <see cref="WaitForInitialStatusMessage"/> can return.
    /// </summary>
    private void OnCoreChannelsPopulatedSerial(object? sender, ChannelsPopulatedEventArgs e)
    {
        _initialStatusReceivedSource?.TrySetResult(true);
        OnCoreChannelsPopulated(sender, e);
    }

    /// <summary>
    /// Handles non-status messages received from Core's DaqifiDevice and routes them
    /// to the protocol handler for streaming data processing.
    /// Status messages are handled via <see cref="OnCoreChannelsPopulatedSerial"/>.
    /// </summary>
    private void OnCoreMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        var args = new MessageEventArgs<object>(e.Message);
        HandleInboundMessage(args);
    }

    private void WaitForInitialStatusMessage()
    {
        if (_coreDevice == null)
        {
            throw new InvalidOperationException("Core device was not initialized.");
        }

        var statusReceivedSource = _initialStatusReceivedSource
            ?? throw new InvalidOperationException("Initial status wait source was not initialized.");

        var deadline = DateTime.UtcNow + InitialStatusTimeout;
        var nextDeviceInfoRequestAt = DateTime.UtcNow + InitialStatusRequestInterval;

        while (DateTime.UtcNow < deadline)
        {
            if (statusReceivedSource.Task.Wait(InitialStatusPollInterval))
            {
                return;
            }

            if (DateTime.UtcNow < nextDeviceInfoRequestAt)
            {
                continue;
            }

            try
            {
                _coreDevice.Send(ScpiMessageProducer.GetDeviceInfo);
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Failed to re-request device info on {PortName}: {ex.Message}");
            }

            nextDeviceInfoRequestAt = DateTime.UtcNow + InitialStatusRequestInterval;
        }

        throw new TimeoutException(
            $"Device on {PortName} did not report status within {InitialStatusTimeout.TotalSeconds:F0} seconds of connect.");
    }

    /// <summary>
    /// Sends a message to the device using Core's DaqifiDevice.
    /// </summary>
    protected override void SendMessage(IOutboundMessage<string> message)
    {
        if (_coreDevice == null || !_coreDevice.IsConnected)
        {
            AppLogger.Warning($"Cannot send to {PortName}: Core device not connected");
            return;
        }
        _coreDevice.Send(message);
    }

    public override bool Write(string command)
    {
        try
        {
            // Use transport's stream for raw writes when available
            if (_transport?.IsConnected == true)
            {
                var bytes = System.Text.Encoding.ASCII.GetBytes(command);
                _transport.Stream.Write(bytes, 0, bytes.Length);
                return true;
            }

            // Fallback to Port for legacy/discovery scenarios
            if (Port?.IsOpen == true)
            {
                Port.WriteTimeout = 1000;
                Port.Write(command);
                return true;
            }

            AppLogger.Warning($"Cannot write to {PortName}: no connection available");
            return false;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to write in SerialStreamingDevice");
            return false;
        }
    }

    public override bool Disconnect()
    {
        try
        {
            StopStreaming();

            // Unsubscribe from Core device events
            if (_coreDevice != null)
            {
                _coreDevice.ChannelsPopulated -= OnCoreChannelsPopulatedSerial;
                _coreDevice.MessageReceived -= OnCoreMessageReceived;
            }

            // Clear channels to prevent ghost channels on reconnect (Issue #29)
            AppLogger.Information($"Cleared {DataChannels.Count} channels for device {DeviceSerialNo}");
            DataChannels.Clear();

            CleanupConnection();
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Error during disconnect");
            return false;
        }
    }

    private void CleanupConnection()
    {
        _initialStatusReceivedSource = null;

        // Unsubscribe from Core device events first
        if (_coreDevice != null)
        {
            try
            {
                _coreDevice.ChannelsPopulated -= OnCoreChannelsPopulatedSerial;
                _coreDevice.MessageReceived -= OnCoreMessageReceived;
                _coreDevice.Disconnect();
                _coreDevice.Dispose();
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Error disconnecting Core device during cleanup: {ex.Message}");
            }
            _coreDevice = null;
        }

        // Clean up transport (this handles the actual serial port)
        if (_transport != null)
        {
            try
            {
                _transport.Disconnect();
                _transport.Dispose();
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Error disconnecting transport during cleanup: {ex.Message}");
            }
            _transport = null;
        }

        // Note: Port cleanup is now handled by transport
    }

    #endregion

    #region Serial Device Only Methods
    public void SendScpiMessage(IOutboundMessage<string> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        SendMessage(message);
    }

    internal Daqifi.Core.Device.IStreamingDevice GetRequiredCoreStreamingDevice()
    {
        return _coreDevice ?? throw new InvalidOperationException(
            $"Core streaming device for {PortName} is not connected.");
    }

    public bool EnableLanUpdateMode()
    {
        AppLogger.Information($"Preparing {PortName} for WiFi firmware mode.");
        if (_coreDevice == null || !_coreDevice.IsConnected)
        {
            AppLogger.Warning($"Cannot prepare {PortName} for WiFi firmware mode: core device is not connected.");
            return false;
        }

        // Power on the WiFi module and set the FW-update-requested flag.
        // APPLY (SYSTem:COMMunicate:LAN:APPLY) is intentionally NOT sent here.
        // The APPLY that triggers bridge-mode initialisation is sent later — via a raw serial
        // port write inside WifiPromptDelayProcessRunner — at the moment the flash tool shows
        // its "Power cycle WINC" prompt.  Sending APPLY here (before the flash tool starts)
        // introduces a race: the WiFi deinit/reinit cycle can take several seconds, and the
        // bridge may not be ready by the time the flash tool's programming phase begins.
        // Sending it at the prompt gives the firmware a guaranteed ~2 s window to initialise
        // the bridge before the tool issues its first serial query.
        Thread.Sleep(2000);
        AppLogger.Information("Sending LAN FW update prep command: SYSTem:POWer:STATe 1");
        _coreDevice.Send(ScpiMessageProducer.TurnDeviceOn);
        Thread.Sleep(1000);
        AppLogger.Information("Sending LAN FW update prep command: SYSTem:COMMUnicate:LAN:FWUpdate");
        _coreDevice.Send(ScpiMessageProducer.SetLanFirmwareUpdateMode);
        return true;
    }

    public void ResetLanAfterUpdate()
    {
        _coreDevice?.Send(ScpiMessageProducer.SetUsbTransparencyMode(0));
        _coreDevice?.Send(ScpiMessageProducer.EnableNetworkLan);
        _coreDevice?.Send(ScpiMessageProducer.ApplyNetworkLan);
        _coreDevice?.Send(ScpiMessageProducer.SaveNetworkLan);
    }

    /// <summary>
    /// Queries the WiFi module chip information by delegating to the underlying Core device.
    /// </summary>
    public Task<LanChipInfo?> GetLanChipInfoAsync(CancellationToken cancellationToken = default)
    {
        if (_coreDevice is not ILanChipInfoProvider provider)
            return Task.FromResult<LanChipInfo?>(null);
        return provider.GetLanChipInfoAsync(cancellationToken);
    }

    /// <summary>
    /// Returns the COM port name for this USB device
    /// </summary>
    protected override string GetUsbDisplayIdentifier()
    {
        try
        {
            var name = Port?.PortName;
            return string.IsNullOrWhiteSpace(name) ? "USB" : name;
        }
        catch
        {
            return "USB";
        }
    }
    #endregion
}
