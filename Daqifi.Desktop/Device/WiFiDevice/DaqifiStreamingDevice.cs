using Daqifi.Desktop.DataModel.Device;
using Daqifi.Desktop.IO.Messages.Consumers;
using Daqifi.Desktop.IO.Messages.Producers;
using System;
using System.Net.Sockets;

namespace Daqifi.Desktop.Device.WiFiDevice
{
    public class DaqifiStreamingDevice : AbstractStreamingDevice
    {
        #region Properties

        public TcpClient Client { get; set; }
        public string IpAddress { get; set; }
        public string MacAddress { get; set; }
        public bool IsPowerOn { get; set; }

        #endregion

        #region Constructor
        public DaqifiStreamingDevice(DeviceInfo deviceInfo)
        {
            Name = deviceInfo.DeviceName;
            IpAddress = deviceInfo.IpAddress;
            MacAddress = deviceInfo.MacAddress;
            IsPowerOn = deviceInfo.IsPowerOn;
            IsStreaming = false;
        }

        #endregion

        #region Override Methods
        public override bool Connect()
        {
            try
            {
                Client = new TcpClient(IpAddress, 9760);
                MessageProducer = new MessageProducer(Client.GetStream());
                MessageProducer.Start();

                TurnOffEcho();
                StopStreaming();
                TurnDeviceOn();
                SetProtobufMessageFormat();

                MessageConsumer = new MessageConsumer(Client.GetStream());
                ((MessageConsumer)MessageConsumer).IsWifiDevice = true;
                ((MessageConsumer)MessageConsumer).ClearBuffer();
                MessageConsumer.Start();

                InitializeDeviceState();
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Problem with connectiong to DAQiFi Device.");
                return false;
            }
        }

        public override bool Disconnect()
        {
            try
            {
                StopStreaming();
                MessageProducer.Stop();
                MessageConsumer.Stop();
                Client.Close();
                Client.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Problem with Disconnectiong from DAQifi Device.");
                return false;
            }
        }
        #endregion

        #region Object overrides
        public override string ToString()
        {
            return Name;
        }

        public override bool Equals(object obj)
        {
            if (!(obj is DaqifiStreamingDevice other)) return false;
            if (Name != other.Name) return false;
            if (IpAddress != other.IpAddress) return false;
            if (MacAddress != other.MacAddress) return false;
            return true;
        }
        #endregion
    }
}
