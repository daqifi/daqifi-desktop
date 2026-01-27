using System.IO.Ports;
using Daqifi.Desktop.Bootloader;
using Daqifi.Core.Device;
using Daqifi.Core.Communication.Transport;
using Daqifi.Core.Communication.Messages;
using Daqifi.Core.Device.Protocol;
using Daqifi.Desktop.IO.Messages;
using ScpiMessageProducer = Daqifi.Core.Communication.Producers.ScpiMessageProducer;

namespace Daqifi.Desktop.Device.SerialDevice;

public class SerialStreamingDevice : AbstractStreamingDevice, IFirmwareUpdateDevice
{
    #region Properties
    private SerialPort? _port;
    private DaqifiDevice? _coreDevice;
    private SerialStreamTransport? _transport;

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

    /// <summary>
    /// Disable base class device info request since Core handles initialization via InitializeAsync.
    /// </summary>
    protected override bool RequestDeviceInfoOnInitialize => false;
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
        DeviceSerialNo = serialNumber ?? string.Empty;
        DeviceVersion = firmwareVersion ?? string.Empty;
    }

    #endregion

    #region Device Info Discovery

    // Temporary Core device used only during TryGetDeviceInfo discovery
    private DaqifiDevice? _discoveryDevice;
    private SerialStreamTransport? _discoveryTransport;

    /// <summary>
    /// Attempts to quickly connect and retrieve device information for discovery purposes.
    /// Returns true if successful, false if device is busy or connection failed.
    /// </summary>
    public bool TryGetDeviceInfo()
    {
        // Check if this device is already connected
        var connectionManager = ConnectionManager.Instance;
        if (connectionManager.ConnectedDevices.Any(d => d is SerialStreamingDevice serial &&
                                                       serial.Port.PortName == Port.PortName))
        {
            return false; // Device is already connected, don't interfere
        }

        try
        {
            // Use Core's transport for discovery (same as Connect() uses for streaming)
            _discoveryTransport = new SerialStreamTransport(Port.PortName, enableDtr: true);
            _discoveryTransport.Connect();

            // Longer delay to let device wake up and stabilize
            // Suppressed: Thread.Sleep required for hardware timing - device power-on sequence
            Thread.Sleep(1000); // Device needs time to power on and initialize

            // Create Core device with transport for both sending and receiving
            _discoveryDevice = new DaqifiDevice("Discovery", _discoveryTransport);
            _discoveryDevice.Connect();

            // Set up a temporary status handler to get device info
            var deviceInfoReceived = false;
            var timeout = DateTime.Now.AddSeconds(4); // Increased timeout for device wake-up

            void StatusHandler(object? sender, MessageReceivedEventArgs e)
            {
                try
                {
                    if (e.Message.Data is DaqifiOutMessage message)
                    {
                        // Use Core's protocol handler logic to determine if this is a status message
                        var messageType = ProtobufProtocolHandler.DetectMessageType(message);
                        if (messageType == ProtobufMessageType.Status)
                        {
                            HydrateDeviceMetadata(message);
                            // Set Name to device part number if available, otherwise keep port name
                            if (!string.IsNullOrWhiteSpace(DevicePartNumber))
                            {
                                Name = DevicePartNumber;
                            }
                            deviceInfoReceived = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"Error processing device info message: {ex.Message}");
                }
            }

            _discoveryDevice.MessageReceived += StatusHandler;

            // Initialize the device (sends echo off, stop streaming, turn on, set protobuf format)
            _discoveryDevice.InitializeAsync().GetAwaiter().GetResult();

            // Request device info with retry logic
            var retryCount = 0;
            var maxRetries = 3;
            var lastRequestTime = DateTime.MinValue;

            while (!deviceInfoReceived && DateTime.Now < timeout)
            {
                // Send GetDeviceInfo request every 1 second, up to maxRetries times
                if (DateTime.Now - lastRequestTime > TimeSpan.FromSeconds(1) && retryCount < maxRetries)
                {
                    try
                    {
                        _discoveryDevice.Send(ScpiMessageProducer.GetDeviceInfo);
                        lastRequestTime = DateTime.Now;
                        retryCount++;
                        AppLogger.Information($"Requesting device info (attempt {retryCount}/{maxRetries}) for port {Port.PortName}");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warning($"Failed to send GetDeviceInfo command: {ex.Message}");
                    }
                }

                // Suppressed: Thread.Sleep required for device communication polling
                Thread.Sleep(100); // Check more frequently for response
            }

            // Unsubscribe before cleanup
            _discoveryDevice.MessageReceived -= StatusHandler;

            // Clean up the quick connection
            QuickDisconnect();

            if (deviceInfoReceived)
            {
                AppLogger.Information($"Successfully retrieved device info for {Port.PortName}: {Name} (S/N: {DeviceSerialNo}, FW: {DeviceVersion})");
            }
            else
            {
                AppLogger.Information($"Could not retrieve device info for {Port.PortName} - device may be off or not responding");
            }

            return deviceInfoReceived;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"Failed to get device info for port {Port.PortName}");
            try
            {
                QuickDisconnect();
            }
            catch (Exception disconnectEx)
            {
                AppLogger.Warning($"Error during cleanup disconnect for port {Port.PortName}: {disconnectEx.Message}");
            }
            return false;
        }
    }

    private void QuickDisconnect()
    {
        // Clean up discovery Core device
        if (_discoveryDevice != null)
        {
            try
            {
                _discoveryDevice.Disconnect();
                _discoveryDevice.Dispose();
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Error disconnecting discovery device: {ex.Message}");
            }
            _discoveryDevice = null;
        }

        // Clean up discovery transport
        if (_discoveryTransport != null)
        {
            try
            {
                _discoveryTransport.Disconnect();
                _discoveryTransport.Dispose();
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Error disconnecting discovery transport: {ex.Message}");
            }
            _discoveryTransport = null;
        }
    }

    #endregion

    #region Override Methods
    public override bool Connect()
    {
        // Ensure any previous connection state is cleaned up first
        CleanupConnection();

        try
        {
            // Use Core's transport for unified message handling (both send and receive)
            // Note: Transport manages the actual SerialPort connection internally
            _transport = new SerialStreamTransport(Port.PortName, enableDtr: true);
            _transport.Connect();

            // Create Core device with transport - this enables both sending AND receiving
            _coreDevice = new DaqifiDevice(
                string.IsNullOrWhiteSpace(Name) ? "DAQiFi Serial Device" : Name,
                _transport);
            _coreDevice.Connect();

            // Subscribe directly to Core's message events (like WiFi device does)
            // This avoids the UI freeze caused by Desktop's MessageConsumer stop/start cycle
            _coreDevice.MessageReceived += OnCoreMessageReceived;

            InitializeDeviceState();

            // Use Core's async initialization (safe because Connect() is called from Task.Run)
            _coreDevice.InitializeAsync().GetAwaiter().GetResult();
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
    /// Handles messages received from Core's DaqifiDevice and routes them to the protocol handler.
    /// </summary>
    private void OnCoreMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        // Core's message is already an IInboundMessage<object>, wrap it for Desktop's event args
        var args = new MessageEventArgs<object>(e.Message);
        HandleInboundMessage(args);
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
        // Unsubscribe from Core device events first
        if (_coreDevice != null)
        {
            try
            {
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

        // Clean up Desktop's MessageConsumer if it was created for SD card operations
        if (MessageConsumer != null)
        {
            try
            {
                MessageConsumer.Stop();
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Error stopping message consumer during cleanup: {ex.Message}");
            }
            MessageConsumer = null;
        }

        // Note: Port cleanup is now handled by transport
    }

    #endregion

    #region Serial Device Only Methods
    public void EnableLanUpdateMode()
    {
        _coreDevice?.Send(ScpiMessageProducer.TurnDeviceOn);
        _coreDevice?.Send(ScpiMessageProducer.SetLanFirmwareUpdateMode);
        _coreDevice?.Send(ScpiMessageProducer.ApplyNetworkLan);
    }

    public void ResetLanAfterUpdate()
    {
        _coreDevice?.Send(ScpiMessageProducer.SetUsbTransparencyMode(0));
        _coreDevice?.Send(ScpiMessageProducer.EnableNetworkLan);
        _coreDevice?.Send(ScpiMessageProducer.ApplyNetworkLan);
        _coreDevice?.Send(ScpiMessageProducer.SaveNetworkLan);
    }

    public void ForceBootloader()
    {
        _coreDevice?.Send(ScpiMessageProducer.ForceBootloader);
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