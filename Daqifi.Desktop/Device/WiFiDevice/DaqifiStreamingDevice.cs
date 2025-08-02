using Daqifi.Desktop.DataModel.Device;
using Daqifi.Desktop.IO.Messages.Consumers;
using Daqifi.Desktop.IO.Messages.Producers;
using System.Net.Sockets;
using Daqifi.Core.Integration.Desktop;
using Daqifi.Core.Communication.Consumers;
using Daqifi.Core.Communication.Transport;
using Daqifi.Core.Communication.Messages;

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
            // Create CoreDeviceAdapter for TCP connection
            _coreAdapter = CoreDeviceAdapter.CreateTcpAdapter(IpAddress, Port);
            
            // Wire up event handlers to maintain existing behavior
            _coreAdapter.MessageReceived += OnCoreAdapterMessageReceived;
            _coreAdapter.ConnectionStatusChanged += OnCoreAdapterConnectionStatusChanged;
            _coreAdapter.ErrorOccurred += OnCoreAdapterErrorOccurred;
            
            // Attempt connection with timeout
            if (!_coreAdapter.Connect())
            {
                AppLogger.Error("Failed to connect to DAQiFi Device using CoreDeviceAdapter.");
                return false;
            }

            // Setup legacy MessageProducer and MessageConsumer to maintain compatibility
            // Get the underlying stream from the adapter for legacy components
            Client = new TcpClient();
            var result = Client.BeginConnect(IpAddress, Port, null, null);
            var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5));

            if (!success)
            {
                AppLogger.Error("Timeout connecting for legacy components.");
                _coreAdapter?.Disconnect();
                return false;
            }

            MessageProducer = new MessageProducer(Client.GetStream());
            MessageProducer.Start();

            TurnOffEcho();
            StopStreaming();
            TurnDeviceOn();
            SetProtobufMessageFormat();

            var stream = Client.GetStream();
            MessageConsumer = new MessageConsumer(stream);
            ((MessageConsumer)MessageConsumer).IsWifiDevice = true;
            if (stream.DataAvailable)
            {
                ((MessageConsumer)MessageConsumer).ClearBuffer();
            }

            MessageConsumer.Start();
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
            // Use CoreDeviceAdapter for writing if available
            if (_coreAdapter != null)
            {
                return _coreAdapter.Write(command);
            }
            
            // Fallback to legacy method
            if (MessageProducer != null)
            {
                var scpiMessage = new ScpiMessage(command);
                MessageProducer.Send(scpiMessage);
                return true;
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
            
            // Disconnect CoreDeviceAdapter first
            if (_coreAdapter != null)
            {
                _coreAdapter.MessageReceived -= OnCoreAdapterMessageReceived;
                _coreAdapter.ConnectionStatusChanged -= OnCoreAdapterConnectionStatusChanged;
                _coreAdapter.ErrorOccurred -= OnCoreAdapterErrorOccurred;
                _coreAdapter.Disconnect();
                _coreAdapter = null;
            }
            
            // Clean up legacy components
            MessageProducer?.Stop();
            MessageConsumer?.Stop();
            if (Client != null)
            {
                Client.Close();
                Client.Dispose();
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
            // Forward messages to existing message handling system
            AppLogger.Information($"[CORE_ADAPTER] Received message: {e.Message.Data}");
            
            // In a full migration, we would process messages here directly
            // For now, this provides logging and can be extended as needed
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