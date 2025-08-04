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
using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.DataModel.Channel;
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
            
            _coreAdapter = CoreDeviceAdapter.CreateTcpAdapter(IpAddress, Port);
            
            // Subscribe to CoreDeviceAdapter events
            _coreAdapter.MessageReceived += OnCoreAdapterMessageReceived;
            _coreAdapter.ConnectionStatusChanged += OnCoreAdapterConnectionStatusChanged;
            _coreAdapter.ErrorOccurred += OnCoreAdapterErrorOccurred;
            
            // Connect using CoreDeviceAdapter
            var connected = _coreAdapter.Connect();
            if (!connected)
            {
                AppLogger.Error("Failed to connect using CoreDeviceAdapter");
                return false;
            }
            
            // Send device initialization commands using CoreDeviceAdapter
            _coreAdapter.Write(ScpiMessageProducer.DisableDeviceEcho.Data);
            _coreAdapter.Write(ScpiMessageProducer.StopStreaming.Data);
            _coreAdapter.Write(ScpiMessageProducer.TurnDeviceOn.Data);
            _coreAdapter.Write(ScpiMessageProducer.SetProtobufStreamFormat.Data);
            
            // Request device info to populate metadata and channels
            AppLogger.Information("[CORE_ADAPTER] Sending GetDeviceInfo command to populate channels");
            _coreAdapter.Write(ScpiMessageProducer.GetDeviceInfo.Data);
            
            // Give some time for the device to respond and populate channels
            Thread.Sleep(2000);
            
            AppLogger.Information($"WiFi device connected successfully using CoreDeviceAdapter v0.4.1 - Channels: {DataChannels.Count}");
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
                return _coreAdapter.Write(command);
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
                _coreAdapter.Disconnect();
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
    
    private void OnCoreAdapterMessageReceived(object sender, MessageReceivedEventArgs<object> e)
    {
        try
        {
            var messageData = e.Message.Data;
            AppLogger.Information($"[CORE_ADAPTER] *** MESSAGE RECEIVED *** Type: {messageData?.GetType().Name}, Timestamp: {e.Timestamp}");
            
            // Handle different message types
            switch (messageData)
            {
                case string textMessage:
                    AppLogger.Information($"[CORE_ADAPTER] Text response: {textMessage.Substring(0, Math.Min(100, textMessage.Length))}...");
                    break;
                    
                case DaqifiOutMessage protobufMessage:
                    AppLogger.Information("[CORE_ADAPTER] Processing protobuf device status message");
                    
                    if (IsValidStatusMessage(protobufMessage))
                    {
                        // Process device metadata
                        HydrateDeviceMetadata(protobufMessage);
                        
                        // Populate channels
                        PopulateChannelsFromMessage(protobufMessage);
                        
                        AppLogger.Information($"[CORE_ADAPTER] Device initialized with {DataChannels.Count} channels");
                    }
                    break;
                    
                default:
                    AppLogger.Information($"[CORE_ADAPTER] Unknown message type: {messageData?.GetType()}");
                    break;
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