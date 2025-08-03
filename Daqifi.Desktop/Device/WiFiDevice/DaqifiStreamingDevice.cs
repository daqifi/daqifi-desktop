using Daqifi.Desktop.DataModel.Device;
using Daqifi.Desktop.IO.Messages.Consumers;
using Daqifi.Desktop.IO.Messages.Producers;
using System.Net.Sockets;
using Daqifi.Core.Communication.Messages;
using Daqifi.Core.Integration.Desktop;
using Daqifi.Core.Communication.Transport;
using Daqifi.Core.Communication.Consumers;

namespace Daqifi.Desktop.Device.WiFiDevice;

public class DaqifiStreamingDevice : AbstractStreamingDevice
{
    #region Properties

    public TcpClient Client { get; set; }
    public string IpAddress { get; set; }
    public string MacAddress { get; set; }
    public int Port { get; set; }
    public bool IsPowerOn { get; set; }
    public string DeviceSerialNo { get; set; }
    public string DeviceVersion { get; set; }
    public override ConnectionType ConnectionType => ConnectionType.Wifi;
    
    private CoreDeviceAdapter? _coreAdapter;
    

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
            // Phase 2: Full CoreDeviceAdapter Integration
            _coreAdapter = CoreDeviceAdapter.CreateTcpAdapter(IpAddress, Port);
            
            // Wire up event handlers BEFORE connecting
            _coreAdapter.ConnectionStatusChanged += OnCoreAdapterConnectionStatusChanged;
            _coreAdapter.MessageReceived += OnCoreAdapterMessageReceived;
            _coreAdapter.ErrorOccurred += OnCoreAdapterErrorOccurred;
            
            // Attempt connection
            if (!_coreAdapter.Connect())
            {
                AppLogger.Error("Failed to connect to DAQiFi Device using CoreDeviceAdapter.");
                return false;
            }

            // Create a bridge MessageConsumer for compatibility with existing message handling
            // This allows existing device initialization and channel discovery to work
            if (_coreAdapter.DataStream != null)
            {
                MessageConsumer = new MessageConsumer(_coreAdapter.DataStream);
                ((MessageConsumer)MessageConsumer).IsWifiDevice = true;
                MessageConsumer.Start();
            }
            
            // Send device initialization commands through CoreDeviceAdapter
            TurnOffEcho();
            StopStreaming();
            TurnDeviceOn();
            SetProtobufMessageFormat();

            InitializeDeviceState();
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Problem with connecting to DAQiFi Device.");
            _coreAdapter?.Disconnect();
            return false;
        }
    }

    public override bool Write(string command)
    {
        try
        {
            // Use CoreDeviceAdapter for writing
            if (_coreAdapter != null)
            {
                return _coreAdapter.Write(command);
            }
            
            return false;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"Failed to write command: {command}");
            return false;
        }
    }

    public override bool Disconnect()
    {
        try
        {
            StopStreaming();
            
            // Clean up bridge MessageConsumer
            MessageConsumer?.Stop();
            
            // Disconnect CoreDeviceAdapter
            if (_coreAdapter != null)
            {
                // Unsubscribe from events in reverse order
                _coreAdapter.ErrorOccurred -= OnCoreAdapterErrorOccurred;
                _coreAdapter.MessageReceived -= OnCoreAdapterMessageReceived;
                _coreAdapter.ConnectionStatusChanged -= OnCoreAdapterConnectionStatusChanged;
                
                _coreAdapter.Disconnect();
                _coreAdapter.Dispose();
                _coreAdapter = null;
            }
            
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Problem with Disconnecting from DAQifi Device.");
            return false;
        }
    }
    #endregion

    #region Object overrides
    public override string ToString()
    {
        return Name;
    }

    public override bool Equals(object obj)
    {
        if (!(obj is DaqifiStreamingDevice other)) { return false; }
        if (Name != other.Name) { return false; }
        if (IpAddress != other.IpAddress) { return false; }
        if (MacAddress != other.MacAddress) { return false; }
        return true;
    }
    #endregion

    #region CoreDeviceAdapter Event Handlers
    
    private void OnCoreAdapterMessageReceived(object? sender, MessageReceivedEventArgs<string> e)
    {
        try
        {
            // Process messages directly through CoreDeviceAdapter events
            // This replaces the legacy MessageConsumer pattern
            AppLogger.Information($"[CORE_ADAPTER] Received message: {e.Message.Data}");
            
            // TODO: For Phase 3, process protobuf messages directly here
            // For now, we'll just log that we received a message
            var message = e.Message.Data;
            if (!string.IsNullOrEmpty(message))
            {
                AppLogger.Information($"[CORE_ADAPTER] Processing message of length: {message.Length}");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "[CORE_ADAPTER] Error processing received message");
        }
    }
    
    private void OnCoreAdapterConnectionStatusChanged(object? sender, TransportStatusEventArgs e)
    {
        try
        {
            AppLogger.Information($"[CORE_ADAPTER] Connection status changed to: {e.IsConnected}");
            
            // Handle connection state changes
            if (!e.IsConnected)
            {
                AppLogger.Warning("[CORE_ADAPTER] Device disconnected");
            }
            else
            {
                AppLogger.Information("[CORE_ADAPTER] Device connected successfully");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "[CORE_ADAPTER] Error handling connection status change");
        }
    }
    
    private void OnCoreAdapterErrorOccurred(object? sender, MessageConsumerErrorEventArgs e)
    {
        try
        {
            AppLogger.Error($"[CORE_ADAPTER] Error occurred: {e.Error?.Message ?? "Unknown error"}");
            
            // Handle errors from the CoreDeviceAdapter
            if (e.Error != null)
            {
                AppLogger.Error(e.Error, "[CORE_ADAPTER] Exception details");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "[CORE_ADAPTER] Error handling adapter error event");
        }
    }
    
    #endregion

}