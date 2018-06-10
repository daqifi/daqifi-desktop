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
        #endregion

        #region Constructor
        public DaqifiStreamingDevice(string name, string macAddress, string ipAddress)
        {
            Name = name;
            MacAddress = macAddress;
            IpAddress = ipAddress;
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
                TurnOffEcho();
                StopStreaming();
                MessageConsumer = new MessageConsumer(Client.GetStream());
                ((MessageConsumer)MessageConsumer).IsWifiDevice = true;
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
                MessageConsumer.Stop();
                Client.Close();
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
