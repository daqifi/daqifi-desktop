using Daqifi.Desktop.IO.Messages.Consumers;
using Daqifi.Desktop.IO.Messages.Producers;
using System.IO.Ports;
using Daqifi.Desktop.Bootloader;
using ScpiMessageProducer = Daqifi.Core.Communication.Producers.ScpiMessageProducer;
using Daqifi.Desktop.IO.Messages;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Core.Communication.Messages;
using Daqifi.Desktop.Channel;
using Daqifi.Desktop.DataModel.Channel;
using Daqifi.Core.Integration.Desktop;
using Daqifi.Core.Communication.Transport;
using Daqifi.Core.Communication.Consumers;

namespace Daqifi.Desktop.Device.SerialDevice;

public class SerialStreamingDevice : AbstractStreamingDevice, IFirmwareUpdateDevice
{
    #region Properties
    public SerialPort Port { get; set; }
    public override ConnectionType ConnectionType => ConnectionType.Usb;
    
    private CoreDeviceAdapter? _coreAdapter;
    
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
            // Quick connection attempt with shorter timeouts for discovery
            Port.ReadTimeout = 1000; // Increased for device wake-up
            Port.WriteTimeout = 1000;
            Port.Open();
            Port.DtrEnable = true;

            // Longer delay to let device wake up and stabilize
            // Suppressed: Thread.Sleep required for hardware timing - device power-on sequence
            Thread.Sleep(1000); // Device needs time to power on and initialize

            MessageProducer = new MessageProducer(Port.BaseStream);
            MessageProducer.Start();

            TurnOffEcho();
            StopStreaming();
            TurnDeviceOn();
            SetProtobufMessageFormat();

            MessageConsumer = new MessageConsumer(Port.BaseStream);
            MessageConsumer.Start();

            // Set up a temporary status handler to get device info
            var deviceInfoReceived = false;
            var timeout = DateTime.Now.AddSeconds(4); // Increased timeout for device wake-up

            Daqifi.Desktop.IO.Messages.Consumers.OnMessageReceivedHandler handler = null;
            handler = (sender, args) =>
            {
                try
                {
                    if (args.Message.Data is DaqifiOutMessage message && IsValidStatusMessage(message))
                    {
                        HydrateDeviceMetadata(message);
                        // Set Name to device part number if available, otherwise keep port name
                        if (!string.IsNullOrWhiteSpace(DevicePartNumber))
                        {
                            Name = DevicePartNumber;
                        }
                        deviceInfoReceived = true;
                        // Remove handler to prevent multiple calls
                        MessageConsumer.OnMessageReceived -= handler;
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"Error processing device info message: {ex.Message}");
                }
            };

            MessageConsumer.OnMessageReceived += handler;

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
                        Write(ScpiMessageProducer.GetDeviceInfo.Data);
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
            // Phase 2: Full CoreDeviceAdapter Integration
            _coreAdapter = CoreDeviceAdapter.CreateSerialAdapter(Port.PortName, 115200);
            
            // Wire up event handlers BEFORE connecting
            _coreAdapter.ConnectionStatusChanged += OnCoreAdapterConnectionStatusChanged;
            _coreAdapter.MessageReceived += OnCoreAdapterMessageReceived;
            _coreAdapter.ErrorOccurred += OnCoreAdapterErrorOccurred;
            
            // Attempt connection
            if (!_coreAdapter.Connect())
            {
                AppLogger.Error("Failed to connect to Serial Device using CoreDeviceAdapter.");
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
            AppLogger.Error(ex, "Failed to connect SerialStreamingDevice");
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
            
            // Fallback: direct port write if CoreDeviceAdapter not available
            if (Port != null && Port.IsOpen)
            {
                Port.WriteTimeout = 1000;
                Port.Write(command);
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"Failed to write command in SerialStreamingDevice: {command}");
            return false;
        }
    }

    public override bool Disconnect()
    {
        try
        {
            // First stop streaming to prevent new data from being requested
            StopStreaming();
            
            // No MessageConsumer to clean up - using CoreDeviceAdapter events only
            
            // Disconnect CoreDeviceAdapter
            if (_coreAdapter != null)
            {
                // Re-enable echo before disconnecting
                try
                {
                    Write(ScpiMessageProducer.EnableDeviceEcho.Data);
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"Error re-enabling echo: {ex.Message}");
                }
                
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
            AppLogger.Error(ex, "Error during device disconnect");
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