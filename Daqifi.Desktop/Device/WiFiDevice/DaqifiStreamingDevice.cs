using Daqifi.Desktop.DataModel.Device;
using Daqifi.Desktop.IO.Messages.Consumers;
using Daqifi.Desktop.IO.Messages.Producers;
using System.Net.Sockets;

namespace Daqifi.Desktop.Device.WiFiDevice
{
    public class DaqifiStreamingDevice : AbstractStreamingDevice
    {
        #region Properties

        public TcpClient Client { get; set; }
        public string IpAddress { get; set; }
        public string MacAddress { get; set; }
        public int Port { get; set; }
        public bool IsPowerOn { get; set; }
        public string DeviceSerialNo { get; set; }
        public string DeviceVersion { get; set; }
        public override ConnectionType ConnectionType => ConnectionType.Wifi;

        #endregion

        #region Constructor
        public DaqifiStreamingDevice(DeviceInfo deviceInfo)
        {
            Name = deviceInfo.DeviceName;
            DeviceSerialNo = deviceInfo.DeviceSerialNo;
            IpAddress = deviceInfo.IpAddress;
            MacAddress = deviceInfo.MacAddress;
            Port = (int)deviceInfo.Port;
            IsPowerOn = deviceInfo.IsPowerOn;
            IsStreaming = false;
            DeviceVersion = deviceInfo.DeviceVersion;

        }

        #endregion

        #region Override Methods
        public override bool Connect()
        {
            try
            {
                Client = new TcpClient();
                var result = Client.BeginConnect(IpAddress, Port, null, null);
                var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5));

                if (!success)
                {
                    AppLogger.Error("Timeout connecting to DAQiFi Device.");
                    return false;
                }

                MessageProducer = new MessageProducer(Client.GetStream());
                MessageProducer.Start();

                TurnOffEcho();
                StopStreaming();
                TurnDeviceOn();
                SetProtobufMessageFormat();

                var stream = Client.GetStream();
                MessageConsumer = new MessageConsumer(stream);
                ((MessageConsumer)MessageConsumer).IsWifiDevice = true;
                if (stream.DataAvailable)
                {
                    ((MessageConsumer)MessageConsumer).ClearBuffer();
                }

                MessageConsumer.Start();
                InitializeDeviceState();
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Problem with connecting to DAQiFi Device.");
                return false;
            }
        }

        public override bool Write(string command)
        {
            return true;
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
                AppLogger.Error(ex, "Problem with Disconnecting from DAQifi Device.");
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
            if (!(obj is DaqifiStreamingDevice other)) { return false; }
            if (Name != other.Name) { return false; }
            if (IpAddress != other.IpAddress) { return false; }
            if (MacAddress != other.MacAddress) { return false; }
            return true;
        }
        #endregion
    }
}
