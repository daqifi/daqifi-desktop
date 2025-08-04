using Daqifi.Desktop.IO.Messages.Consumers;
using Daqifi.Desktop.IO.Messages.Producers;
using System.IO.Ports;
using Daqifi.Desktop.Bootloader;
using ScpiMessageProducer = Daqifi.Core.Communication.Producers.ScpiMessageProducer;
using Daqifi.Desktop.IO.Messages;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Core.Communication.Messages;
using Daqifi.Core.Integration.Desktop;
using Daqifi.Core.Communication.Transport;
using Daqifi.Core.Communication.Consumers;
using Daqifi.Desktop.Channel;
using Daqifi.Desktop.DataModel.Channel;
using Google.Protobuf;

namespace Daqifi.Desktop.Device.SerialDevice;

public class SerialStreamingDevice : AbstractStreamingDevice, IFirmwareUpdateDevice
{
    #region Properties
    public SerialPort Port { get; set; }
    public override ConnectionType ConnectionType => ConnectionType.Usb;
    
    // Phase 2: CoreDeviceAdapter integration
    private CoreDeviceAdapter _coreAdapter;
    
    #endregion

    #region Constructor
    public SerialStreamingDevice(string portName)
    {
        Name = portName;
        Port = new SerialPort(portName);
    }

    #endregion

    #region Device Info Discovery
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
            // Use CoreDeviceAdapter for device discovery instead of legacy MessageConsumer
            using var discoveryAdapter = CoreDeviceAdapter.CreateSerialAdapter(Port.PortName);
            
            var deviceInfoReceived = false;
            var timeout = DateTime.Now.AddSeconds(4);
            
            // Set up temporary message handler for device discovery
            EventHandler<MessageReceivedEventArgs<object>> discoveryHandler = null;
            discoveryHandler = (sender, args) =>
            {
                try
                {
                    var messageData = args.Message.Data;
                    AppLogger.Information($"[DISCOVERY] Received message type: {messageData?.GetType().Name}");
                    
                    if (messageData is DaqifiOutMessage protobufMessage && IsValidStatusMessage(protobufMessage))
                    {
                        AppLogger.Information("[DISCOVERY] Processing device status message for discovery");
                        HydrateDeviceMetadata(protobufMessage);
                        
                        // Set Name to device part number if available, otherwise keep port name
                        if (!string.IsNullOrWhiteSpace(DevicePartNumber))
                        {
                            Name = DevicePartNumber;
                        }
                        deviceInfoReceived = true;
                        
                        // Remove handler to prevent multiple calls
                        discoveryAdapter.MessageReceived -= discoveryHandler;
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"[DISCOVERY] Error processing device info message: {ex.Message}");
                }
            };
            
            discoveryAdapter.MessageReceived += discoveryHandler;
            
            // Add error event handler for discovery debugging
            discoveryAdapter.ErrorOccurred += (sender, args) => {
                AppLogger.Error($"[DISCOVERY] CoreDeviceAdapter error: {args.Error?.Message}");
            };
            
            // Connect using CoreDeviceAdapter
            if (!discoveryAdapter.Connect())
            {
                AppLogger.Warning($"[DISCOVERY] Failed to connect to {Port.PortName} for device discovery");
                return false;
            }
            
            AppLogger.Information($"[DISCOVERY] Connected to {Port.PortName}, IsConnected: {discoveryAdapter.IsConnected}");
            
            // Send device initialization and info request
            discoveryAdapter.Write(ScpiMessageProducer.DisableDeviceEcho.Data);
            discoveryAdapter.Write(ScpiMessageProducer.StopStreaming.Data);
            discoveryAdapter.Write(ScpiMessageProducer.TurnDeviceOn.Data);
            discoveryAdapter.Write(ScpiMessageProducer.SetProtobufStreamFormat.Data);
            
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
                        var writeSuccess = discoveryAdapter.Write(ScpiMessageProducer.GetDeviceInfo.Data);
                        lastRequestTime = DateTime.Now;
                        retryCount++;
                        AppLogger.Information($"[DISCOVERY] Requesting device info (attempt {retryCount}/{maxRetries}) for port {Port.PortName}, Write success: {writeSuccess}, IsConnected: {discoveryAdapter.IsConnected}");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warning($"[DISCOVERY] Failed to send GetDeviceInfo command: {ex.Message}");
                    }
                }
                
                // Suppressed: Thread.Sleep required for device communication polling
                Thread.Sleep(100); // Check more frequently for response
            }

            // Explicit cleanup before using statement auto-disposes
            AppLogger.Information("[DISCOVERY] Cleaning up discovery adapter...");
            discoveryAdapter.MessageReceived -= discoveryHandler;
            discoveryAdapter.Disconnect();
            AppLogger.Information("[DISCOVERY] Discovery adapter cleaned up successfully");
            
            // Give the port time to be released by the OS before using statement disposes
            Thread.Sleep(500);

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
        try
        {
            // Stop message processing first
            if (MessageConsumer != null)
            {
                try
                {
                    MessageConsumer.Stop();
                    Thread.Sleep(50); // Give it time to stop
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"Error stopping message consumer: {ex.Message}");
                }
            }

            if (MessageProducer != null)
            {
                try
                {
                    MessageProducer.Stop();
                    Thread.Sleep(50); // Give it time to stop
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"Error stopping message producer: {ex.Message}");
                }
            }
            
            // Close the port
            if (Port != null && Port.IsOpen)
            {
                try
                {
                    Port.DtrEnable = false;
                    Thread.Sleep(100); // Give DTR time to be processed
                    Port.Close();
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"Error closing serial port: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Error during quick disconnect: {ex.Message}");
        }
        finally
        {
            MessageProducer = null;
            MessageConsumer = null;
        }
    }

    #endregion

    #region Override Methods
    public override bool Connect()
    {
        try
        {
            // Phase 2: Full CoreDeviceAdapter integration with v0.4.1
            // v0.4.1 includes CompositeMessageParser for protobuf support
            
            // Add delay to ensure discovery adapter has fully released the port
            AppLogger.Information($"[CORE_ADAPTER] Waiting for port {Port.PortName} to be available...");
            Thread.Sleep(1000);
            
            _coreAdapter = CoreDeviceAdapter.CreateSerialAdapter(Port.PortName);
            
            // Subscribe to CoreDeviceAdapter events
            _coreAdapter.MessageReceived += OnCoreAdapterMessageReceived;
            _coreAdapter.ConnectionStatusChanged += OnCoreAdapterConnectionStatusChanged;
            _coreAdapter.ErrorOccurred += OnCoreAdapterErrorOccurred;
            
            // Add error event handler for connection debugging
            _coreAdapter.ErrorOccurred += (sender, args) => {
                AppLogger.Error($"[CORE_ADAPTER] Connection error: {args.Error?.Message}");
            };
            
            // Connect using CoreDeviceAdapter
            var connected = _coreAdapter.Connect();
            AppLogger.Information($"[CORE_ADAPTER] Connect() returned: {connected}, IsConnected: {_coreAdapter.IsConnected}");
            
            if (!connected || !_coreAdapter.IsConnected)
            {
                AppLogger.Error($"Failed to connect using CoreDeviceAdapter. Connect result: {connected}, IsConnected: {_coreAdapter.IsConnected}");
                return false;
            }
            
            // Check if MessageProducer and MessageConsumer are initialized
            var hasProducer = _coreAdapter.MessageProducer != null;
            var hasConsumer = _coreAdapter.MessageConsumer != null;
            AppLogger.Information($"[CORE_ADAPTER] MessageProducer initialized: {hasProducer}, MessageConsumer initialized: {hasConsumer}");
            
            if (!hasProducer || !hasConsumer)
            {
                AppLogger.Error("[CORE_ADAPTER] MessageProducer or MessageConsumer not initialized after connection");
                return false;
            }
            
            // Send device initialization commands using CoreDeviceAdapter
            AppLogger.Information($"[CORE_ADAPTER] Sending SCPI commands:");
            AppLogger.Information($"  DisableDeviceEcho: '{ScpiMessageProducer.DisableDeviceEcho.Data}'");
            AppLogger.Information($"  StopStreaming: '{ScpiMessageProducer.StopStreaming.Data}'");
            AppLogger.Information($"  TurnDeviceOn: '{ScpiMessageProducer.TurnDeviceOn.Data}'");
            AppLogger.Information($"  SetProtobufStreamFormat: '{ScpiMessageProducer.SetProtobufStreamFormat.Data}'");
            
            var cmd1 = _coreAdapter.Write(ScpiMessageProducer.DisableDeviceEcho.Data);
            var cmd2 = _coreAdapter.Write(ScpiMessageProducer.StopStreaming.Data);
            var cmd3 = _coreAdapter.Write(ScpiMessageProducer.TurnDeviceOn.Data);
            var cmd4 = _coreAdapter.Write(ScpiMessageProducer.SetProtobufStreamFormat.Data);
            
            AppLogger.Information($"[CORE_ADAPTER] Command results - Echo: {cmd1}, Stop: {cmd2}, TurnOn: {cmd3}, Protobuf: {cmd4}");
            
            // Test with multiple simple commands to see if device responds
            AppLogger.Information("[CORE_ADAPTER] Testing basic device communication...");
            
            var idnCmd = _coreAdapter.Write("*IDN?");
            AppLogger.Information($"[CORE_ADAPTER] *IDN? Write result: {idnCmd}");
            Thread.Sleep(500);
            
            var testCmd = _coreAdapter.Write("*TST?");
            AppLogger.Information($"[CORE_ADAPTER] *TST? Write result: {testCmd}");
            Thread.Sleep(500);
            
            var statusCmd = _coreAdapter.Write("*ESR?");
            AppLogger.Information($"[CORE_ADAPTER] *ESR? Write result: {statusCmd}");
            Thread.Sleep(1000);
            
            // Request device info to populate metadata and channels
            AppLogger.Information("[CORE_ADAPTER] Sending GetDeviceInfo command to populate channels");
            AppLogger.Information($"  GetDeviceInfo command: '{ScpiMessageProducer.GetDeviceInfo.Data}'");
            var infoCmd = _coreAdapter.Write(ScpiMessageProducer.GetDeviceInfo.Data);
            AppLogger.Information($"[CORE_ADAPTER] GetDeviceInfo Write result: {infoCmd}");
            
            // Give some time for the device to respond and populate channels
            Thread.Sleep(2000);
            
            AppLogger.Information($"Serial device connected successfully using CoreDeviceAdapter v0.4.1 - Channels: {DataChannels.Count}");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to connect SerialStreamingDevice using CoreDeviceAdapter");
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
            AppLogger.Error(ex, "Error during device disconnect using CoreDeviceAdapter");
            return false;
        }
    }

    #endregion
        
    #region Serial Device Only Methods
    public void EnableLanUpdateMode()
    {
        Write(ScpiMessageProducer.TurnDeviceOn.Data);
        Write(ScpiMessageProducer.SetLanFirmwareUpdateMode.Data);
        Write(ScpiMessageProducer.ApplyNetworkLan.Data);
    }
        
    public void ResetLanAfterUpdate()
    {
        Write(ScpiMessageProducer.SetUsbTransparencyMode(0).Data);
        Write(ScpiMessageProducer.EnableNetworkLan.Data);
        Write(ScpiMessageProducer.ApplyNetworkLan.Data);
        Write(ScpiMessageProducer.SaveNetworkLan.Data);
    }
    
    public void ForceBootloader()
    {
        Write(ScpiMessageProducer.ForceBootloader.Data);
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