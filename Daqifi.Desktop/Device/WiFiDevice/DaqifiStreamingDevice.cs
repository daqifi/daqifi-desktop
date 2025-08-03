using Daqifi.Desktop.DataModel.Device;
using Daqifi.Desktop.IO.Messages.Consumers;
using Daqifi.Desktop.IO.Messages.Producers;
using System.Net.Sockets;
using Daqifi.Core.Communication.Messages;
using Daqifi.Desktop.IO.Messages;
using ScpiMessageProducer = Daqifi.Core.Communication.Producers.ScpiMessageProducer;
using Daqifi.Core.Integration.Desktop;
using Daqifi.Core.Communication.Transport;
using Daqifi.Core.Communication.Consumers;
using Daqifi.Desktop.Device.Channel;
using Daqifi.Desktop.Common.Loggers;
using Google.Protobuf;

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
    
    // Phase 2: CoreDeviceAdapter integration
    private CoreDeviceAdapter _coreAdapter;
    
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
            // Phase 2: Full CoreDeviceAdapter integration with v0.4.1
            // v0.4.1 includes CompositeMessageParser for protobuf support
            
            var tcpTransport = new TcpTransport(IpAddress, Port);
            _coreAdapter = new CoreDeviceAdapter(tcpTransport);
            
            // Subscribe to CoreDeviceAdapter events
            _coreAdapter.MessageReceived += OnCoreAdapterMessageReceived;
            _coreAdapter.ConnectionStatusChanged += OnCoreAdapterConnectionStatusChanged;
            _coreAdapter.ErrorOccurred += OnCoreAdapterErrorOccurred;
            
            // Connect using CoreDeviceAdapter
            var connected = _coreAdapter.ConnectAsync().Result;
            if (!connected)
            {
                AppLogger.Error("Failed to connect using CoreDeviceAdapter");
                return false;
            }
            
            // Send device initialization commands using CoreDeviceAdapter
            _coreAdapter.SendAsync(ScpiMessageProducer.DisableDeviceEcho.Data).Wait();
            _coreAdapter.SendAsync(ScpiMessageProducer.StopDevice.Data).Wait();
            _coreAdapter.SendAsync(ScpiMessageProducer.TurnDeviceOn.Data).Wait();
            _coreAdapter.SendAsync(ScpiMessageProducer.SetProtobufMessageFormat.Data).Wait();
            
            // Request device info to populate metadata and channels
            _coreAdapter.SendAsync(ScpiMessageProducer.GetDeviceInfo.Data).Wait();
            
            AppLogger.Information("WiFi device connected successfully using CoreDeviceAdapter v0.4.1");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Problem with connecting to DAQiFi Device using CoreDeviceAdapter.");
            return false;
        }
    }

    public override bool Write(string command)
    {
        try
        {
            // Phase 2: Use CoreDeviceAdapter for all communication
            if (_coreAdapter != null)
            {
                _coreAdapter.SendAsync(command).Wait();
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"Failed to write command using CoreDeviceAdapter: {command}");
            return false;
        }
    }

    public override bool Disconnect()
    {
        try
        {
            StopStreaming();
            
            // Phase 2: Clean up CoreDeviceAdapter
            if (_coreAdapter != null)
            {
                // Unsubscribe from events
                _coreAdapter.MessageReceived -= OnCoreAdapterMessageReceived;
                _coreAdapter.ConnectionStatusChanged -= OnCoreAdapterConnectionStatusChanged;
                _coreAdapter.ErrorOccurred -= OnCoreAdapterErrorOccurred;
                
                // Disconnect and dispose
                _coreAdapter.DisconnectAsync().Wait();
                _coreAdapter.Dispose();
                _coreAdapter = null;
            }
            
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Problem with Disconnecting from DAQifi Device using CoreDeviceAdapter.");
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
    
    private void OnCoreAdapterMessageReceived(object sender, MessageReceivedEventArgs<string> e)
    {
        try
        {
            AppLogger.Information($"[CORE_ADAPTER] Received message: {e.Data?.Substring(0, Math.Min(100, e.Data.Length ?? 0))}...");
            
            if (string.IsNullOrEmpty(e.Data))
                return;
                
            // Try to parse as protobuf message
            try
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(e.Data);
                using var stream = new System.IO.MemoryStream(bytes);
                var outMessage = DaqifiOutMessage.Parser.ParseDelimitedFrom(stream);
                
                if (outMessage != null && IsValidStatusMessage(outMessage))
                {
                    AppLogger.Information("[CORE_ADAPTER] Processing device status message");
                    
                    // Process device metadata
                    HydrateDeviceMetadata(outMessage);
                    
                    // Populate channels
                    PopulateChannelsFromMessage(outMessage);
                    
                    AppLogger.Information($"[CORE_ADAPTER] Device initialized with {DataChannels.Count} channels");
                }
            }
            catch (Exception parseEx)
            {
                AppLogger.Debug($"[CORE_ADAPTER] Message not protobuf format: {parseEx.Message}");
                // This might be a text response, which is normal for some commands
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "[CORE_ADAPTER] Error processing received message");
        }
    }
    
    private void OnCoreAdapterConnectionStatusChanged(object sender, TransportStatusEventArgs e)
    {
        try
        {
            AppLogger.Information($"[CORE_ADAPTER] Connection status changed to: {e.IsConnected}");
            
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
    
    private void OnCoreAdapterErrorOccurred(object sender, MessageConsumerErrorEventArgs e)
    {
        try
        {
            AppLogger.Error($"[CORE_ADAPTER] Error occurred: {e.Error?.Message ?? "Unknown error"}");
            
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
    
    private void PopulateChannelsFromMessage(DaqifiOutMessage outMessage)
    {
        try
        {
            // Clear existing channels
            DataChannels.Clear();
            
            // Digital channels
            if (outMessage.DigitalPortNum > 0)
            {
                for (var i = 0; i < outMessage.DigitalPortNum; i++)
                {
                    DataChannels.Add(new DigitalChannel(this, "DIO" + i, i, ChannelDirection.Input, true));
                }
            }
            
            // Analog input channels
            if (outMessage.AnalogInPortNum > 0)
            {
                var analogInPortRanges = outMessage.AnalogInPortRange;
                var analogInCalibrationBValues = outMessage.AnalogInCalB;
                var analogInCalibrationMValues = outMessage.AnalogInCalM;
                var analogInInternalScaleMValues = outMessage.AnalogInIntScaleM;
                var analogInResolution = outMessage.AnalogInRes;

                Func<IList<float>, int, float, float> getWithDefault = (IList<float> list, int idx, float def) =>
                {
                    if (list.Count > idx) return list[idx];
                    return def;
                };

                for (var i = 0; i < outMessage.AnalogInPortNum; i++)
                {
                    DataChannels.Add(new AnalogChannel(this, "AI" + i, i, ChannelDirection.Input, false,
                        getWithDefault(analogInCalibrationBValues, i, 0.0f),
                        getWithDefault(analogInCalibrationMValues, i, 1.0f),
                        getWithDefault(analogInInternalScaleMValues, i, 1.0f),
                        getWithDefault(analogInPortRanges, i, 1.0f),
                        analogInResolution));
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "[CORE_ADAPTER] Error populating channels from message");
        }
    }
    
    #endregion

}