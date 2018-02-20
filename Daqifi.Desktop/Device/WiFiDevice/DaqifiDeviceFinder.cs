using Daqifi.Desktop.Message;
using Daqifi.Desktop.Message.Consumers;
using System;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Daqifi.Desktop.Communication.Protobuf;

namespace Daqifi.Desktop.Device.WiFiDevice
{
    public class DaqifiDeviceFinder : AbstractMessageConsumer, IDeviceFinder
    {
        #region Private Data

        private const string DaqifiFinderQuery = "DAQiFi?\r\n";
        private const string NativeFinderQuery = "Discovery: Who is out there?\r\n";
        private const string PowerEvent = "Power event occurred"; // TODO check if this is still needed
        private readonly byte[] _queryCommandBytes = Encoding.ASCII.GetBytes(DaqifiFinderQuery);

        #endregion

        #region Properties

        public UdpClient Client { get; }
        public IPEndPoint Destination { get; }

        #endregion

        #region Events

        public event OnDeviceFoundHandler OnDeviceFound;
        public event OnDeviceRemovedHandler OnDeviceRemoved;

        #endregion

        #region Constructor

        public DaqifiDeviceFinder(int broadcastPort)
        {
            try
            {
                Destination = new IPEndPoint(GetBroadcastAddress(), broadcastPort);
                Client = new UdpClient(broadcastPort);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Error in DaqifiDeviceFinder");
            }
        }

        #endregion

        #region AbstractMessageConsumer overrides

        public override void Run()
        {
            try
            {
                Client.EnableBroadcast = true;
                Client.BeginReceive(HandleFinderMessageReceived, null);

                while (Running)
                {
                    Client.Send(_queryCommandBytes, _queryCommandBytes.Length, Destination);
                    Thread.Sleep(1000);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Error in DaqifiDeviceFinder");
            }
        }

        #endregion

        public override void Stop()
        {
            try
            {
                if (Client != null)
                {
                    Running = false;
                    Client.Close();
                }
                base.Stop();
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Error Stopping Device Finder");
            }
        }

        private void HandleFinderMessageReceived(IAsyncResult res)
        {
            try
            {
                var remoteIpEndPoint = new IPEndPoint(IPAddress.Any, 8000);
                var receivedBytes = Client.EndReceive(res, ref remoteIpEndPoint);
                var receivedText = Encoding.ASCII.GetString(receivedBytes);

                if (IsValidDiscoveryMessage(receivedText))
                {
                    var stream = new MemoryStream(receivedBytes);
                    var message = DaqifiOutMessage.ParseDelimitedFrom(stream);
                    var device = GetDeviceFromProtobufMessage(message);
                    NotifyDeviceFound(this, device);
                }

                Client.BeginReceive(HandleFinderMessageReceived, null);
            }
            catch (ObjectDisposedException)
            {
                // hide this exception for now. TODO find a better way.
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Problem in DaqifiDeviceFinder");
            }
        }

        private bool IsValidDiscoveryMessage(string receivedText)
        {
            return !receivedText.Contains(NativeFinderQuery) &&
                   !receivedText.Contains(DaqifiFinderQuery) &&
                   !receivedText.Contains(PowerEvent);
        }

        private IDevice GetDeviceFromProtobufMessage(IDaqifiOutMessage message)
        {
            var hostName = message.HostName;
            var macAddress = ProtobufDecoder.GetMacAddressString(message);
            var ipAddress = ProtobufDecoder.GetIpAddressString(message);
            var device = new DaqifiStreamingDevice(hostName, macAddress, ipAddress);

            if (message.HasSsid)
            {
                device.NetworkConfiguration.Ssid = message.Ssid;
            }

            return device;
        }


        public void NotifyDeviceFound(object sender, IDevice device)
        {
            OnDeviceFound?.Invoke(sender, device);
        }

        public void NotifyDeviceRemoved(object sender, IDevice device)
        {
            OnDeviceRemoved?.Invoke(sender, device);
        }

        // TODO move to its own helper class
        private IPAddress GetBroadcastAddress()
        {
            var address = IPAddress.Broadcast;
            var subnet = IPAddress.None;
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    address = ip;
                }
            }

            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                foreach (var unicastIpAddressInformation in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    if (unicastIpAddressInformation.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (address.Equals(unicastIpAddressInformation.Address))
                    {
                        subnet = unicastIpAddressInformation.IPv4Mask;
                    }
                }
            }

            var broadcastAddress = new byte[address.GetAddressBytes().Length];
            for (var i = 0; i < broadcastAddress.Length; i++)
            {
                broadcastAddress[i] = (byte) (address.GetAddressBytes()[i] | (subnet.GetAddressBytes()[i] ^ 255));
            }
            return new IPAddress(broadcastAddress);
        }
    }
}
