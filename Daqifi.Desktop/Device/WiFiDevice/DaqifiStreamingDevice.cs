using Daqifi.Core.Communication.Messages;
using Daqifi.Core.Communication.Transport;
using Daqifi.Core.Device;
using Daqifi.Desktop.DataModel.Device;
using Daqifi.Desktop.IO.Messages;
using CoreStreamingDevice = Daqifi.Core.Device.DaqifiStreamingDevice;

namespace Daqifi.Desktop.Device.WiFiDevice;

/// <summary>
/// WiFi streaming device that uses Core's DaqifiDevice directly for communication.
/// Unlike USB devices, WiFi devices don't need consumer swapping because SD card operations
/// are only available over USB.
/// </summary>
public class DaqifiStreamingDevice : AbstractStreamingDevice
{
    #region Private Fields
    private CoreStreamingDevice? _coreDevice;
    #endregion

    #region Properties

    public int Port { get; set; }
    public bool IsPowerOn { get; set; }
    public override ConnectionType ConnectionType => ConnectionType.Wifi;
    public override bool IsConnected => _coreDevice?.IsConnected == true;
    protected override bool RequestDeviceInfoOnInitialize => false;
    protected override CoreStreamingDevice? CoreDeviceForStreaming => _coreDevice;

    #endregion

    #region Constructor
    public DaqifiStreamingDevice(DeviceInfo deviceInfo)
    {
        Name = deviceInfo.DeviceName;
        DeviceSerialNo = deviceInfo.DeviceSerialNo;
        IpAddress = deviceInfo.IpAddress;
        MacAddress = deviceInfo.MacAddress;
        Port = (int)deviceInfo.Port;
        IsPowerOn = deviceInfo.IsPowerOn;
        IsStreaming = false;
        DeviceVersion = deviceInfo.DeviceVersion;
    }

    #endregion

    #region Override Methods
    public override bool Connect()
    {
        try
        {
            var options = new DeviceConnectionOptions
            {
                DeviceName = string.IsNullOrWhiteSpace(Name) ? "DAQiFi Device" : Name,
                ConnectionRetry = ConnectionRetryOptions.NoRetry,
                InitializeDevice = false
            };

            var connectedDevice = DaqifiDeviceFactory.ConnectTcp(IpAddress, Port, options);
            _coreDevice = connectedDevice as CoreStreamingDevice;
            if (_coreDevice == null)
            {
                connectedDevice.Dispose();
                throw new InvalidOperationException("Connected Core device does not support streaming operations.");
            }

            if (!_coreDevice.IsConnected)
            {
                AppLogger.Error($"Failed to connect to DAQiFi device at {IpAddress}:{Port}");
                return false;
            }

            // Subscribe directly to Core's message events
            _coreDevice.MessageReceived += OnCoreMessageReceived;

            InitializeDeviceState();

            // Use async initialization (safe because Connect() is called from Task.Run)
            _coreDevice.InitializeAsync().GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"Problem with connecting to DAQiFi Device at {IpAddress}:{Port}");

            // Clean up partially initialized device
            if (_coreDevice != null)
            {
                try
                {
                    _coreDevice.MessageReceived -= OnCoreMessageReceived;
                    _coreDevice.Disconnect();
                    _coreDevice.Dispose();
                }
                catch (Exception cleanupEx)
                {
                    AppLogger.Warning($"Error during connection cleanup: {cleanupEx.Message}");
                }
                _coreDevice = null;
            }

            return false;
        }
    }

    public override bool Write(string command)
    {
        return true;
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

            _coreDevice?.Disconnect();
            _coreDevice?.Dispose();
            _coreDevice = null;
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Problem with Disconnecting from DAQiFi Device.");
            return false;
        }
    }

    /// <summary>
    /// Sends messages directly through Core's DaqifiDevice instead of using adapters.
    /// </summary>
    protected override void SendMessage(IOutboundMessage<string> message)
    {
        if (_coreDevice == null || !_coreDevice.IsConnected)
        {
            AppLogger.Warning($"Cannot send message to {IpAddress}:{Port}: Core device is not connected (MessageType={message?.GetType().Name})");
            return;
        }

        try
        {
            _coreDevice.Send(message);
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"Failed to send message to {IpAddress}:{Port} (MessageType={message?.GetType().Name})");
        }
    }
    #endregion

    #region Event Handlers
    /// <summary>
    /// Handles messages received from Core's DaqifiDevice and routes them to the protocol handler.
    /// </summary>
    private void OnCoreMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        // Core's message is already an IInboundMessage<object>, wrap it for Desktop's event args
        var args = new MessageEventArgs<object>(e.Message);
        HandleInboundMessage(args);
    }
    #endregion

    #region Object overrides
    public override string ToString()
    {
        return Name;
    }

    public override bool Equals(object obj)
    {
        if (obj is not DaqifiStreamingDevice other) { return false; }
        if (Name != other.Name) { return false; }
        if (IpAddress != other.IpAddress) { return false; }
        if (MacAddress != other.MacAddress) { return false; }
        return true;
    }
    #endregion
}
