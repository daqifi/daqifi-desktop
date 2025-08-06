using Daqifi.Desktop.IO.Messages.Consumers;
using Daqifi.Desktop.IO.Messages.Producers;
using System.IO.Ports;
using Daqifi.Desktop.Bootloader;
using ScpiMessageProducer = Daqifi.Core.Communication.Producers.ScpiMessageProducer;
using Daqifi.Desktop.IO.Messages;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Core.Integration.Desktop;
using Daqifi.Desktop.Device.Core;

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
            AppLogger.Information($"[CORE_SERIAL] TryGetDeviceInfo: Starting discovery connection to {Port.PortName}");
            
            // Create CoreDeviceAdapter with DelimitedProtobufMessageParser for discovery
            var parser = new DelimitedProtobufMessageParser();
            _coreAdapter = CoreDeviceAdapter.CreateSerialAdapter(Port.PortName, 115200, parser);
            
            AppLogger.Information($"[CORE_SERIAL] TryGetDeviceInfo: Created CoreDeviceAdapter");
            
            // Connect using Core
            if (!_coreAdapter.Connect())
            {
                AppLogger.Error("Failed to connect via CoreDeviceAdapter during discovery");
                return false;
            }
            
            AppLogger.Information($"[CORE_SERIAL] TryGetDeviceInfo: Connected successfully");

            // Create wrapper classes to bridge Core interfaces to Desktop interfaces
            MessageProducer = new CoreMessageProducerWrapper(_coreAdapter);
            MessageConsumer = new CoreMessageConsumerWrapper(_coreAdapter);
            
            MessageProducer.Start();
            MessageConsumer.Start();

            // Longer delay to let device wake up and stabilize
            // Suppressed: Thread.Sleep required for hardware timing - device power-on sequence
            Thread.Sleep(1000); // Device needs time to power on and initialize

            TurnOffEcho();
            StopStreaming();
            TurnDeviceOn();
            SetProtobufMessageFormat();

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
                        MessageProducer.Send(ScpiMessageProducer.GetDeviceInfo);
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
            
            // Add a small delay to ensure port is fully released before potential reconnection
            Thread.Sleep(100);

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
            
            // Disconnect and dispose CoreDeviceAdapter
            if (_coreAdapter != null)
            {
                try
                {
                    _coreAdapter.Disconnect();
                    _coreAdapter.Dispose();
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"Error disconnecting CoreDeviceAdapter: {ex.Message}");
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
            _coreAdapter = null;
        }
    }

    #endregion

    #region Override Methods
    public override bool Connect()
    {
        try
        {
            AppLogger.Information($"[CORE_SERIAL] Starting connection to {Port.PortName}");
            
            // Create CoreDeviceAdapter with DelimitedProtobufMessageParser
            var parser = new DelimitedProtobufMessageParser();
            _coreAdapter = CoreDeviceAdapter.CreateSerialAdapter(Port.PortName, 115200, parser);
            
            AppLogger.Information($"[CORE_SERIAL] Created CoreDeviceAdapter for {Port.PortName}");
            
            // Connect using Core
            try
            {
                var connectResult = _coreAdapter.Connect();
                AppLogger.Information($"[CORE_SERIAL] Connect result: {connectResult}");
                
                if (!connectResult)
                {
                    AppLogger.Error($"[CORE_SERIAL] CoreDeviceAdapter.Connect() returned false");
                    AppLogger.Error($"[CORE_SERIAL] Transport IsConnected: {_coreAdapter.Transport?.IsConnected}");
                    AppLogger.Error($"[CORE_SERIAL] Transport Info: {_coreAdapter.Transport?.ConnectionInfo}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, $"[CORE_SERIAL] Exception during CoreDeviceAdapter.Connect()");
                return false;
            }
            
            AppLogger.Information($"[CORE_SERIAL] CoreDeviceAdapter connected successfully");
            AppLogger.Information($"[CORE_SERIAL] IsConnected: {_coreAdapter.IsConnected}");
            AppLogger.Information($"[CORE_SERIAL] ConnectionInfo: {_coreAdapter.ConnectionInfo}");

            // Create wrapper classes to bridge Core interfaces to Desktop interfaces
            MessageProducer = new CoreMessageProducerWrapper(_coreAdapter);
            MessageConsumer = new CoreMessageConsumerWrapper(_coreAdapter);
            
            AppLogger.Information($"[CORE_SERIAL] Created wrapper classes");
            AppLogger.Information($"[CORE_SERIAL] CoreAdapter.MessageProducer null: {_coreAdapter.MessageProducer == null}");
            AppLogger.Information($"[CORE_SERIAL] CoreAdapter.MessageConsumer null: {_coreAdapter.MessageConsumer == null}");
            
            MessageProducer.Start();
            MessageConsumer.Start();
            
            AppLogger.Information($"[CORE_SERIAL] Started message producer and consumer");

            // Send initialization commands
            TurnOffEcho();
            StopStreaming();
            TurnDeviceOn();   
            SetProtobufMessageFormat();

            InitializeDeviceState();
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to connect SerialStreamingDevice via CoreDeviceAdapter");
            _coreAdapter?.Dispose();
            _coreAdapter = null;
            return false;
        }
    }

    public override bool Write(string command)
    {
        try
        {
            if (_coreAdapter == null)
            {
                AppLogger.Error("CoreDeviceAdapter is null in Write method");
                return false;
            }
            
            return _coreAdapter.Write(command);
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to write via CoreDeviceAdapter");
            return false;
        }
    }

    public override bool Disconnect()
    {
        try
        {
            // First stop streaming to prevent new data from being requested
            StopStreaming();
                
            // Stop the message producer first to prevent new messages
            if (MessageProducer != null)
            {
                try
                {
                    MessageProducer.Send(ScpiMessageProducer.EnableDeviceEcho);
                    MessageProducer.StopSafely(); // Use StopSafely to ensure queued messages are sent
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"Error stopping message producer: {ex.Message}");
                }
            }

            // Stop the consumer next
            if (MessageConsumer != null)
            {
                try
                {
                    MessageConsumer.Stop();
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"Error stopping message consumer: {ex.Message}");
                }
            }

            // Disconnect and dispose CoreDeviceAdapter
            if (_coreAdapter != null)
            {
                try
                {
                    _coreAdapter.Disconnect();
                    _coreAdapter.Dispose();
                    _coreAdapter = null;
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"Error disconnecting CoreDeviceAdapter: {ex.Message}");
                }
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
        MessageProducer.Send(ScpiMessageProducer.TurnDeviceOn);
        MessageProducer.Send(ScpiMessageProducer.SetLanFirmwareUpdateMode);
        MessageProducer.Send(ScpiMessageProducer.ApplyNetworkLan);
    }
        
    public void ResetLanAfterUpdate()
    {
        MessageProducer.Send(ScpiMessageProducer.SetUsbTransparencyMode(0));
        MessageProducer.Send(ScpiMessageProducer.EnableNetworkLan);
        MessageProducer.Send(ScpiMessageProducer.ApplyNetworkLan);
        MessageProducer.Send(ScpiMessageProducer.SaveNetworkLan);
    }
    
    public void ForceBootloader()
    {
        MessageProducer.Send(ScpiMessageProducer.ForceBootloader);
    }
    #endregion
}