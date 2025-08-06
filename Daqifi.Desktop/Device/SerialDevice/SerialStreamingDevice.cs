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
    private CoreDeviceAdapter? _coreAdapter;
    public override ConnectionType ConnectionType => ConnectionType.Usb;
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
            // Create temporary CoreDeviceAdapter with delimited protobuf parser for DAQiFi devices  
            // Try 9600 baud first (legacy default) then 115200 if that fails
            var delimitedParser = new DelimitedProtobufMessageParser();
            using var tempAdapter = CoreDeviceAdapter.CreateSerialAdapter(Port.PortName, 9600, delimitedParser);
            AppLogger.Information($"[BAUD_TEST] Trying connection with 9600 baud on {Port.PortName}");
            
            var connected = tempAdapter.Connect();
            if (!connected)
            {
                AppLogger.Information($"Could not connect to device on port {Port.PortName} during discovery");
                return false;
            }

            // Set up temporary wrappers for device info discovery
            MessageProducer = new CoreMessageProducerWrapper(tempAdapter);
            MessageConsumer = new CoreMessageConsumerWrapper(tempAdapter);
            MessageConsumer.Start();

            TurnOffEcho();
            StopStreaming();
            TurnDeviceOn();
            SetProtobufMessageFormat();

            // Give device time to process initialization commands
            Thread.Sleep(500);

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

            // Clean up the quick connection - CoreDeviceAdapter handles this automatically with 'using'
            MessageProducer = null;
            MessageConsumer = null;

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
            MessageProducer = null;
            MessageConsumer = null;
            return false;
        }
    }


    #endregion

    #region Override Methods
    public override bool Connect()
    {
        try
        {
            // Create CoreDeviceAdapter for Serial connection with delimited protobuf parser
            // Use 9600 baud (legacy SerialPort default) instead of 115200
            var delimitedParser = new DelimitedProtobufMessageParser();
            _coreAdapter = CoreDeviceAdapter.CreateSerialAdapter(Port.PortName, 9600, delimitedParser);
            AppLogger.Information($"[BAUD_TEST] Connecting with 9600 baud on {Port.PortName}");
            
            // Connect using Core adapter
            var connected = _coreAdapter.Connect();
            if (!connected)
            {
                AppLogger.Error($"Failed to connect to DAQiFi device on port {Port.PortName}");
                return false;
            }

            // Set up compatibility with existing AbstractStreamingDevice expectations
            MessageProducer = new CoreMessageProducerWrapper(_coreAdapter);
            MessageConsumer = new CoreMessageConsumerWrapper(_coreAdapter);

            // Initialize device as per existing pattern
            TurnOffEcho();
            StopStreaming();
            TurnDeviceOn();   
            SetProtobufMessageFormat();

            // Give device time to process initialization commands
            Thread.Sleep(500);

            // Start the message consumer
            MessageConsumer.Start();
            
            // Initialize device state
            InitializeDeviceState();
            
            AppLogger.Information($"Successfully connected to DAQiFi device on port {Port.PortName} using Core adapter");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"Failed to connect SerialStreamingDevice on port {Port.PortName} using Core adapter");
            return false;
        }
    }

    public override bool Write(string command)
    {
        if (_coreAdapter == null)
        {
            AppLogger.Warning("CoreDeviceAdapter is not initialized");
            return false;
        }

        return _coreAdapter.Write(command);
    }

    public override bool Disconnect()
    {
        try
        {
            if (_coreAdapter == null)
            {
                return true; // Already disconnected
            }

            // First stop streaming to prevent new data from being requested
            StopStreaming();
            
            // Stop message consumer
            MessageConsumer?.Stop();

            // Disconnect using Core adapter
            var disconnected = _coreAdapter.Disconnect();
            
            // Clean up
            _coreAdapter.Dispose();
            _coreAdapter = null;
            MessageProducer = null;
            MessageConsumer = null;

            AppLogger.Information($"Successfully disconnected from DAQiFi device on port {Port.PortName}");
            return disconnected;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"Error during device disconnect on port {Port.PortName}");
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