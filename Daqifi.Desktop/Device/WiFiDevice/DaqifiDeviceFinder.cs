using Daqifi.Desktop.Message;
using Daqifi.Desktop.Message.Consumers;
using System;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

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
                //Destination = new IPEndPoint(IPAddress.Broadcast, broadcastPort);

                Destination = new IPEndPoint(GetBroadcastAddress(), broadcastPort);
                Client = new UdpClient(broadcastPort);
            }
            catch(Exception ex)
            {
                AppLogger.Error(ex, "Error creating streamingDevice listener");
            }
        }
        #endregion

        #region AbstractMessageConsumer overrides
        public override void Run()
        {
            Client.EnableBroadcast = true;
            Client.BeginReceive(HandleFinderMessageReceived, null);

            while (Running)
            {
                Client.Send(_queryCommandBytes, _queryCommandBytes.Length, Destination);
                Thread.Sleep(1000);
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

                if (!receivedText.Contains(NativeFinderQuery) &&
                    !receivedText.Contains(DaqifiFinderQuery) &&
                    !receivedText.Contains(PowerEvent))
                {

                    var stream = new MemoryStream(receivedBytes);
                    var message = DaqifiOutMessage.ParseDelimitedFrom(stream);
                    if (message.HasHostName)
                    {
                        var device = new DeviceMessage(message).Device;
                        NotifyDeviceFound(this, device);
                    }

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

        public void NotifyDeviceFound(object sender, IDevice device)
        {
            OnDeviceFound?.Invoke(sender, device);
        }

        public void NotifyDeviceRemoved(object sender, IDevice device)
        {
            OnDeviceRemoved?.Invoke(sender, device);
        }



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
                broadcastAddress[i] = (byte)(address.GetAddressBytes()[i] | (subnet.GetAddressBytes()[i] ^ 255));
            }
            return new IPAddress(broadcastAddress);
        }
    }
}
