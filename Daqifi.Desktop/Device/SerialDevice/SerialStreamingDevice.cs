using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Message.Consumers;
using Daqifi.Desktop.Message.Producers;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;

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

            DataChannels = new List<IChannel>();
        }
        #endregion

        #region Override Methods
        public override bool Connect()
        {
            try
            {
                Port.Open();
                MessageProducer = new MessageProducer(Port.BaseStream);
                Thread.Sleep(100);
                TurnOffEcho();
                Thread.Sleep(100);
                StopStreaming();
                Thread.Sleep(100);
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
                MessageConsumer.Stop();
                StopStreaming();
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
