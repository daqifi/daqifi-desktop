using Daqifi.Desktop.IO.Messages.Consumers;
using Daqifi.Desktop.IO.Messages.Producers;
using System;
using System.IO.Ports;

namespace Daqifi.Desktop.Device.SerialDevice
{
    public class SerialStreamingDevice : AbstractStreamingDevice
    {
        #region Properties
        public SerialPort Port { get; }
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
                MessageProducer.Stop();
                MessageConsumer.Stop();
                StopStreaming();
                Port.DtrEnable = false;
                Port.Close();
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion
        
        #region Serial Device Only Methods
        public void EnableLanUpdateMode()
        {
            MessageProducer.Send(ScpiMessagePoducer.DeviceOn);
            MessageProducer.Send(ScpiMessagePoducer.SetLanFWUpdateMode());
            MessageProducer.Send(ScpiMessagePoducer.ApplyLan());
        }
        
        public void ResetLanAfterUpdate()
        {
            MessageProducer.Send(ScpiMessagePoducer.SetUsbTransparencyMode(0));
            MessageProducer.Send(ScpiMessagePoducer.EnabledLan());
            MessageProducer.Send(ScpiMessagePoducer.ApplyLan());
        }
        #endregion
    }
}
