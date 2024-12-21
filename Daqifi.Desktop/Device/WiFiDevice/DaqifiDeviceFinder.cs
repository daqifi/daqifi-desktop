using Daqifi.Desktop.DataModel.Device;
using Daqifi.Desktop.IO.Messages.Consumers;
using Daqifi.Desktop.IO.Messages.Decoders;
using Daqifi.Desktop.IO.Messages.MessageTypes;
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
                    var message = DaqifiOutMessage.Parser.ParseDelimitedFrom(stream);
                    var device = GetDeviceFromProtobufMessage(message);
                    ((DaqifiStreamingDevice)device).IpAddress = remoteIpEndPoint.Address.ToString();
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

        private IDevice GetDeviceFromProtobufMessage(DaqifiOutMessage message)
        {
            var deviceName = message.HostName;
            var macAddress = ProtobufDecoder.GetMacAddressString(message);
            var ipAddress = ProtobufDecoder.GetIpAddressString(message);
            var isPowerOn = message.PwrStatus == 1;
            var port = message.DevicePort;
            var device_sn = message.DeviceSn;

            var deviceInfo = new DeviceInfo
            {
                DeviceName = deviceName,
                IpAddress = ipAddress,
                MacAddress = macAddress,
                Port = port,
                IsPowerOn = isPowerOn,
                DeviceSerialNo = device_sn.ToString()

            };

            var device = new DaqifiStreamingDevice(deviceInfo);

            if (!string.IsNullOrWhiteSpace(message.Ssid))
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
            IPAddress broadcastAddress = IPAddress.None;
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                    (networkInterface.NetworkInterfaceType != NetworkInterfaceType.Ethernet &&
                     networkInterface.NetworkInterfaceType != NetworkInterfaceType.Wireless80211))
                {
                    continue;
                }
                foreach (var unicastIpAddressInformation in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    if (unicastIpAddressInformation.Address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }
                    var ipAddress = unicastIpAddressInformation.Address;
                    var subnetMask = unicastIpAddressInformation.IPv4Mask;
                    var broadcastBytes = new byte[ipAddress.GetAddressBytes().Length];
                    var ipBytes = ipAddress.GetAddressBytes();
                    var maskBytes = subnetMask.GetAddressBytes();
                    for (var i = 0; i < broadcastBytes.Length; i++)
                    {
                        broadcastBytes[i] = (byte)(ipBytes[i] | (maskBytes[i] ^ 255));
                    }
                    broadcastAddress = new IPAddress(broadcastBytes);
                    break;
                }
                if (broadcastAddress != IPAddress.None)
                {
                    break;
                }
            }
            return broadcastAddress;
        }

    }
}
