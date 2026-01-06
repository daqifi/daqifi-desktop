using Daqifi.Desktop.IO.Messages.Consumers;
using Daqifi.Desktop.IO.Messages.Producers;
using System.IO.Ports;
using Daqifi.Desktop.Bootloader;
using ScpiMessageProducer = Daqifi.Core.Communication.Producers.ScpiMessageProducer;
using Daqifi.Core.Device.Protocol; // Added for ProtobufProtocolHandler
using Daqifi.Core.Communication.Messages; // Added for DaqifiOutMessage

namespace Daqifi.Desktop.Device.SerialDevice;

public class SerialStreamingDevice : AbstractStreamingDevice, IFirmwareUpdateDevice
{
    #region Properties
    private SerialPort? _port;
    
    public SerialPort? Port
    {
        get => _port;
        set
        {
            if (_port != value)
            {
                _port = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayIdentifier));
            }
        }
    }

    /// <summary>
    /// Gets the actual COM port name for UART communication
    /// This is needed because Name may be set to the device part number after discovery
    /// </summary>
    public string PortName => Port?.PortName ?? Name;

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

            // Use async initialization
            InitializeDeviceAsync().GetAwaiter().GetResult();

            MessageConsumer = new MessageConsumer(Port.BaseStream);
            MessageConsumer.Start();

            // Set up a temporary status handler to get device info
            var deviceInfoReceived = false;
            var timeout = DateTime.Now.AddSeconds(4); // Increased timeout for device wake-up

            OnMessageReceivedHandler handler = null;
            handler = (sender, args) =>
            {
                try
                {
                    if (args.Message.Data is DaqifiOutMessage message)
                    {
                        // Use Core's protocol handler logic to determine if this is a status message
                        var messageType = ProtobufProtocolHandler.DetectMessageType(message);
                        if (messageType == ProtobufMessageType.Status)
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
            if (Port is { IsOpen: true })
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
            Task.Delay(1000);
            Port.Open();
            Port.DtrEnable = true;
            MessageProducer = new MessageProducer(Port.BaseStream);
            MessageProducer.Start();

            // Use async initialization (safe because Connect() is called from Task.Run)
            InitializeDeviceAsync().GetAwaiter().GetResult();

            MessageConsumer = new MessageConsumer(Port.BaseStream);
            MessageConsumer.Start();
            InitializeDeviceState();
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to connect SerialStreamingDevice");
            return false;
        }
    }

    public override bool Write(string command)
    {
        try
        {
            Port.WriteTimeout = 1000;
            Port.Write(command);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to write in SerialStreamingDevice");
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

            // Clear channels to prevent ghost channels on reconnect (Issue #29)
            DataChannels.Clear();
            AppLogger.Information($"Cleared {DataChannels.Count} channels for device {DeviceSerialNo}");

            // Finally close the port
            if (Port != null)
            {
                try
                {
                    if (Port.IsOpen)
                    {
                        Port.DtrEnable = false;
                        // Give a small delay to ensure DTR state change is processed
                        Thread.Sleep(50);
                        Port.Close();
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"Error closing serial port: {ex.Message}");
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

    /// <summary>
    /// Returns the COM port name for this USB device
    /// </summary>
    protected override string GetUsbDisplayIdentifier()
    {
        try
        {
            var name = Port?.PortName;
            return string.IsNullOrWhiteSpace(name) ? "USB" : name;
        }
        catch
        {
            return "USB";
        }
    }
    #endregion
}