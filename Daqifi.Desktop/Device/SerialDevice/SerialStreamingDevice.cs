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
                Port.Open();
                Port.DtrEnable = true;
                MessageProducer = new MessageProducer(Port.BaseStream);
                MessageProducer.Start();
                TurnOffEcho();
                StopStreaming();
                TurnDeviceOn();
                MessageConsumer = new MessageConsumer(Port.BaseStream);
                MessageConsumer.Start();
                InitializeDeviceState();
                return true;
            }
            catch (Exception ex)
            {
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
    }
}
