using Daqifi.Desktop.DataModel.Device;
using Daqifi.Desktop.IO.Messages.Consumers;
using Daqifi.Desktop.IO.Messages.Producers;
using System.Net.Sockets;
using Daqifi.Core.Communication.Messages;
using Daqifi.Core.Integration.Desktop;
using Daqifi.Core.Communication.Transport;
using Daqifi.Core.Communication.Consumers;
using Daqifi.Desktop.IO.Messages;
using ScpiMessageProducer = Daqifi.Core.Communication.Producers.ScpiMessageProducer;
using Daqifi.Desktop.Channel;
using Daqifi.Desktop.DataModel.Channel;

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

            // No bridge MessageConsumer needed - we'll use CoreDeviceAdapter events directly
            // This eliminates dual consumer conflicts on the same stream
            
            // Send device initialization commands through CoreDeviceAdapter
            TurnOffEcho();
            StopStreaming();
            TurnDeviceOn();
            SetProtobufMessageFormat();

            // Initialize device state using CoreDeviceAdapter instead of legacy MessageConsumer
            InitializeDeviceStateWithCoreAdapter();
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
            
            // No MessageConsumer to clean up - using CoreDeviceAdapter events only
            
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

    #region CoreDeviceAdapter Device Initialization
    
    /// <summary>
    /// Initialize device state using CoreDeviceAdapter events instead of legacy MessageConsumer
    /// </summary>
    private void InitializeDeviceStateWithCoreAdapter()
    {
        // Send GetDeviceInfo command through CoreDeviceAdapter
        // The response will be handled by OnCoreAdapterMessageReceived event
        Write(ScpiMessageProducer.GetDeviceInfo.Data);
        AppLogger.Information("[CORE_ADAPTER] Sent GetDeviceInfo command for device initialization");
    }
    
    #endregion

    #region CoreDeviceAdapter Event Handlers
    
    private void OnCoreAdapterMessageReceived(object? sender, MessageReceivedEventArgs<string> e)
    {
        try
        {
            // Process messages directly through CoreDeviceAdapter events
            // This replaces the legacy MessageConsumer pattern completely
            var messageData = e.Message.Data;
            AppLogger.Information($"[CORE_ADAPTER] Received message: {messageData}");
            
            if (!string.IsNullOrEmpty(messageData))
            {
                // Try to parse as protobuf message for device initialization
                try
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(messageData);
                    using var stream = new System.IO.MemoryStream(bytes);
                    var outMessage = DaqifiOutMessage.Parser.ParseDelimitedFrom(stream);
                    
                    if (outMessage != null && IsValidStatusMessage(outMessage))
                    {
                        AppLogger.Information("[CORE_ADAPTER] Processing device status message");
                        
                        // Replicate HandleStatusMessageReceived logic
                        HydrateDeviceMetadata(outMessage);
                        
                        // Manually populate channels since the methods are private
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
                        
                        AppLogger.Information($"[CORE_ADAPTER] Device initialized with {DataChannels.Count} channels");
                    }
                }
                catch (Exception parseEx)
                {
                    AppLogger.Warning($"[CORE_ADAPTER] Could not parse as protobuf: {parseEx.Message}");
                    // This might be a text response, which is normal for some commands
                }
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