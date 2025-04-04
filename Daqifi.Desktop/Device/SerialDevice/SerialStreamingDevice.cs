using Daqifi.Desktop.IO.Messages.Consumers;
using Daqifi.Desktop.IO.Messages.Producers;
using System.IO.Ports;
using ScpiMessageProducer = Daqifi.Core.Communication.Producers.ScpiMessageProducer;

namespace Daqifi.Desktop.Device.SerialDevice;

public class SerialStreamingDevice : AbstractStreamingDevice
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