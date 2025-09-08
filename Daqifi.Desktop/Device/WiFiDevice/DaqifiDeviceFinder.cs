using System.Globalization;
using Daqifi.Desktop.DataModel.Device;
using Daqifi.Desktop.IO.Messages.Consumers;
using Daqifi.Desktop.IO.Messages.Decoders;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Daqifi.Desktop.Device.WiFiDevice;

public class DaqifiDeviceFinder : AbstractMessageConsumer, IDeviceFinder
{
    #region Private Data

    private const string DaqifiFinderQuery = "DAQiFi?\r\n";
    private const string NativeFinderQuery = "Discovery: Who is out there?\r\n";
    private const string PowerEvent = "Power event occurred"; // TODO check if this is still needed
    private readonly byte[] _queryCommandBytes = Encoding.ASCII.GetBytes(DaqifiFinderQuery);

    #endregion

    #region Properties

    private UdpClient Client { get; }
    private readonly List<IPEndPoint> _broadcastEndpoints = [];

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
            _broadcastEndpoints = GetAllBroadcastEndpoints(broadcastPort);
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
            if (Client != null && _broadcastEndpoints.Count > 0)
            {
                Client.EnableBroadcast = true;
                Client.BeginReceive(HandleFinderMessageReceived, null);

                while (Running)
                {
                    foreach(var endpoint in _broadcastEndpoints)
                    {
                         try
                         {
                            AppLogger.Information($"Sending UDP broadcast to {endpoint}");
                            Client.Send(_queryCommandBytes, _queryCommandBytes.Length, endpoint);
                         }
                         catch (SocketException sockEx)
                         {
                             AppLogger.Warning($"Error sending broadcast to {endpoint}, {sockEx}");
                         }
                    }
                    Thread.Sleep(1000);
                }
            }
            else if (Client != null)
            {
                 AppLogger.Information("DAQiFi device discovery started, but no suitable network interfaces found for broadcasting.");
                 Client.EnableBroadcast = true;
                 Client.BeginReceive(HandleFinderMessageReceived, null);
                 while(Running) { Thread.Sleep(5000); }
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
            
            AppLogger.Information($"Received UDP message from {remoteIpEndPoint}: {receivedText.Replace("\r\n", "\\r\\n")}");

            if (IsValidDiscoveryMessage(receivedText))
            {
                var stream = new MemoryStream(receivedBytes);
                var message = DaqifiOutMessage.Parser.ParseDelimitedFrom(stream);
                var device = GetDeviceFromProtobufMessage(message);
                device.IpAddress = remoteIpEndPoint.Address.ToString();
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

    private static bool IsValidDiscoveryMessage(string receivedText)
    {
        return !receivedText.Contains(NativeFinderQuery) &&
               !receivedText.Contains(DaqifiFinderQuery) &&
               !receivedText.Contains(PowerEvent);
    }

    private static DaqifiStreamingDevice GetDeviceFromProtobufMessage(DaqifiOutMessage message)
    {
        var deviceName = message.HostName;
        var macAddress = ProtobufDecoder.GetMacAddressString(message);
        var ipAddress = ProtobufDecoder.GetIpAddressString(message);
        var isPowerOn = message.PwrStatus == 1;
        var port = message.DevicePort;
        var deviceSn = message.DeviceSn;
        var deviceVersion = message.DeviceFwRev;

        var deviceInfo = new DeviceInfo
        {
            DeviceName = deviceName,
            IpAddress = ipAddress,
            MacAddress = macAddress,
            Port = port,
            IsPowerOn = isPowerOn,
            DeviceSerialNo = deviceSn.ToString(CultureInfo.InvariantCulture),
            DeviceVersion = deviceVersion
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

    private List<IPEndPoint> GetAllBroadcastEndpoints(int port)
    {
        var endpoints = new List<IPEndPoint>();
        AppLogger.Information("Scanning network interfaces for UDP broadcast...");
        
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            AppLogger.Information($"Interface: {networkInterface.Name}, Type: {networkInterface.NetworkInterfaceType}, Status: {networkInterface.OperationalStatus}");
            
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                !networkInterface.Supports(NetworkInterfaceComponent.IPv4) ||
                (networkInterface.NetworkInterfaceType != NetworkInterfaceType.Ethernet &&
                 networkInterface.NetworkInterfaceType != NetworkInterfaceType.Wireless80211))
            {
                AppLogger.Information($"Skipping interface {networkInterface.Name} - doesn't meet criteria");
                continue;
            }

            var ipProperties = networkInterface.GetIPProperties();
            if (ipProperties == null)
            {
                continue;
            }

            foreach (var unicastIpAddressInformation in ipProperties.UnicastAddresses)
            {
                if (unicastIpAddressInformation.Address.AddressFamily != AddressFamily.InterNetwork ||
                    unicastIpAddressInformation.IPv4Mask == null ||
                    unicastIpAddressInformation.IPv4Mask.Equals(IPAddress.Any))
                {
                    continue;
                }

                var ipAddress = unicastIpAddressInformation.Address;
                var subnetMask = unicastIpAddressInformation.IPv4Mask;

                var ipBytes = ipAddress.GetAddressBytes();
                var maskBytes = subnetMask.GetAddressBytes();
                if (ipBytes.Length != 4 || maskBytes.Length != 4) continue;

                var broadcastBytes = new byte[4];
                for (var i = 0; i < 4; i++)
                {
                    broadcastBytes[i] = (byte)(ipBytes[i] | (maskBytes[i] ^ 255));
                }

                var broadcastAddress = new IPAddress(broadcastBytes);
                var endpoint = new IPEndPoint(broadcastAddress, port);
                endpoints.Add(endpoint);
                
                AppLogger.Information($"Added broadcast endpoint: {endpoint} for interface {networkInterface.Name}");
                break;
            }
        }

        AppLogger.Information(endpoints.Count == 0
            ? "Could not find any suitable network interfaces for DAQiFi discovery broadcast."
            : $"DAQiFi Discovery broadcasting to: {string.Join(", ", endpoints.Select(ep => ep.Address.ToString()))}");

        return endpoints;
    }
}