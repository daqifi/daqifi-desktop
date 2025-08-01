using Daqifi.Desktop.IO.Messages.Consumers;
using Daqifi.Desktop.IO.Messages.Producers;
using System.IO.Ports;
using Daqifi.Desktop.Bootloader;
using ScpiMessageProducer = Daqifi.Core.Communication.Producers.ScpiMessageProducer;
using Daqifi.Desktop.IO.Messages;
using Daqifi.Desktop.Common.Loggers;

namespace Daqifi.Desktop.Device.SerialDevice;

public class SerialStreamingDevice : AbstractStreamingDevice, IFirmwareUpdateDevice
{
    #region Properties
    public SerialPort Port { get; set; }
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
            // Quick connection attempt with shorter timeout
            Port.ReadTimeout = 1000;
            Port.WriteTimeout = 1000;
            Port.Open();
            Port.DtrEnable = true;

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
            var timeout = DateTime.Now.AddSeconds(3);

            MessageConsumer.OnMessageReceived += (sender, args) =>
            {
                if (args.Message.Data is DaqifiOutMessage message && IsValidStatusMessage(message))
                {
                    HydrateDeviceMetadata(message);
                    deviceInfoReceived = true;
                }
            };

            // Request device info
            MessageProducer.Send(ScpiMessageProducer.GetDeviceInfo);

            // Wait for response with timeout
            while (!deviceInfoReceived && DateTime.Now < timeout)
            {
                Thread.Sleep(100);
            }

            // Clean up the quick connection
            QuickDisconnect();

            return deviceInfoReceived;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"Failed to get device info for port {Port.PortName}");
            try { QuickDisconnect(); } catch { }
            return false;
        }
    }

    private void QuickDisconnect()
    {
        try
        {
            MessageProducer?.Stop();
            MessageConsumer?.Stop();
            
            if (Port != null && Port.IsOpen)
            {
                Port.DtrEnable = false;
                Port.Close();
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

    private bool IsValidStatusMessage(DaqifiOutMessage message)
    {
        return (message.DigitalPortNum != 0 || message.AnalogInPortNum != 0 || message.AnalogOutPortNum != 0);
    }

    private void HydrateDeviceMetadata(DaqifiOutMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.DevicePn))
        {
            DevicePartNumber = message.DevicePn;
            Name = message.DevicePn; // Use part number as device name
        }
        if (message.DeviceSn != 0)
        {
            DeviceSerialNo = message.DeviceSn.ToString();
        }
        if (!string.IsNullOrWhiteSpace(message.DeviceFwRev))
        {
            DeviceVersion = message.DeviceFwRev;
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

            TurnOffEcho();
            StopStreaming();
            TurnDeviceOn();   
            SetProtobufMessageFormat();

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
    #endregion
}