using Daqifi.Desktop.DataModel.Device;
using Daqifi.Desktop.IO.Messages.Consumers;
using Daqifi.Desktop.IO.Messages.Decoders;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Daqifi.Desktop.Device.WiFiDevice;

/// <summary>
/// Enhanced device finder that creates separate UDP clients for each network interface
/// to ensure broadcasts are sent from all interfaces
/// </summary>
public class DaqifiDeviceFinderMultiInterface : AbstractMessageConsumer, IDeviceFinder
{
    #region Private Data

    private const string DaqifiFinderQuery = "DAQiFi?\r\n";
    private const string NativeFinderQuery = "Discovery: Who is out there?\r\n";
    private const string PowerEvent = "Power event occurred";
    private readonly byte[] _queryCommandBytes = Encoding.ASCII.GetBytes(DaqifiFinderQuery);
    private readonly int _broadcastPort;

    #endregion

    #region Properties

    private class InterfaceBroadcaster
    {
        public UdpClient Client { get; set; }
        public IPEndPoint BroadcastEndpoint { get; set; }
        public IPAddress InterfaceAddress { get; set; }
        public string InterfaceName { get; set; }
    }

    private readonly List<InterfaceBroadcaster> _broadcasters = [];

    #endregion

    #region Events

    public event OnDeviceFoundHandler OnDeviceFound;
    public event OnDeviceRemovedHandler OnDeviceRemoved;

    #endregion

    #region Constructor

    public DaqifiDeviceFinderMultiInterface(int broadcastPort)
    {
        _broadcastPort = broadcastPort;
        try
        {
            InitializeBroadcasters();
            
            // We don't need a separate receive client - each broadcaster will receive its own responses
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Error in DaqifiDeviceFinderMultiInterface");
        }
    }

    #endregion

    private void InitializeBroadcasters()
    {
        AppLogger.Information("=== Initializing multi-interface UDP broadcasters ===");
        
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                !networkInterface.Supports(NetworkInterfaceComponent.IPv4))
            {
                continue;
            }
            
            // Skip only loopback and tunnel interfaces
            if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
            {
                continue;
            }
            
            var ipProperties = networkInterface.GetIPProperties();
            if (ipProperties == null)
            {
                continue;
            }

            foreach (var unicastAddress in ipProperties.UnicastAddresses)
            {
                if (unicastAddress.Address.AddressFamily != AddressFamily.InterNetwork ||
                    unicastAddress.IPv4Mask == null || 
                    unicastAddress.IPv4Mask.Equals(IPAddress.Any))
                {
                    continue;
                }

                var ipAddress = unicastAddress.Address;
                var subnetMask = unicastAddress.IPv4Mask;
                
                var ipBytes = ipAddress.GetAddressBytes();
                var maskBytes = subnetMask.GetAddressBytes();
                if (ipBytes.Length != 4 || maskBytes.Length != 4) continue;

                var broadcastBytes = new byte[4];
                for (var i = 0; i < 4; i++)
                {
                    broadcastBytes[i] = (byte)(ipBytes[i] | (maskBytes[i] ^ 255));
                }
                
                var broadcastAddress = new IPAddress(broadcastBytes);

                try
                {
                    // Create a UDP client bound to this specific interface
                    var client = new UdpClient(new IPEndPoint(ipAddress, 0));
                    client.EnableBroadcast = true;
                    
                    var broadcaster = new InterfaceBroadcaster
                    {
                        Client = client,
                        BroadcastEndpoint = new IPEndPoint(broadcastAddress, _broadcastPort),
                        InterfaceAddress = ipAddress,
                        InterfaceName = networkInterface.Name
                    };
                    
                    _broadcasters.Add(broadcaster);
                    AppLogger.Information($"Created broadcaster for {networkInterface.Name} ({ipAddress}) -> {broadcastAddress}:{_broadcastPort}");
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"Failed to create UDP client for interface {networkInterface.Name}: {ex.Message}");
                }
                
                break; // Only use first valid IPv4 address per interface
            }
        }

        AppLogger.Information($"=== Created {_broadcasters.Count} UDP broadcasters ===");
    }

    #region AbstractMessageConsumer overrides

    public override void Run()
    {
        try
        {
            if (_broadcasters.Count > 0)
            {
                // Set up receiving on each broadcaster's socket
                foreach (var broadcaster in _broadcasters)
                {
                    broadcaster.Client.BeginReceive(HandleFinderMessageReceived, broadcaster);
                }

                while (Running)
                {
                    foreach (var broadcaster in _broadcasters)
                    {
                        try
                        {
                            broadcaster.Client.Send(_queryCommandBytes, _queryCommandBytes.Length, broadcaster.BroadcastEndpoint);
                            
                            // Log only periodically to avoid spam
                            if (DateTime.Now.Second % 10 == 0)
                            {
                                AppLogger.Information($"Broadcasting from {broadcaster.InterfaceName} ({broadcaster.InterfaceAddress}) to {broadcaster.BroadcastEndpoint}");
                            }
                        }
                        catch (SocketException sockEx)
                        {
                            AppLogger.Warning($"Error broadcasting from {broadcaster.InterfaceName}: {sockEx.Message}");
                        }
                    }
                    Thread.Sleep(1000);
                }
            }
            else
            {
                AppLogger.Information("No suitable network interfaces found for broadcasting.");
                while (Running) { Thread.Sleep(5000); }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Error in DaqifiDeviceFinderMultiInterface");
        }
    }

    #endregion

    public override void Stop()
    {
        try
        {
            Running = false;
            
            // Close all broadcaster clients
            foreach (var broadcaster in _broadcasters)
            {
                try
                {
                    broadcaster.Client?.Close();
                }
                catch { }
            }
            _broadcasters.Clear();
            
            
            base.Stop();
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Error stopping multi-interface device finder");
        }
    }

    private void HandleFinderMessageReceived(IAsyncResult res)
    {
        var broadcaster = res.AsyncState as InterfaceBroadcaster;
        if (broadcaster == null) return;
        
        try
        {
            var remoteIpEndPoint = new IPEndPoint(IPAddress.Any, 0);
            var receivedBytes = broadcaster.Client.EndReceive(res, ref remoteIpEndPoint);
            var receivedText = Encoding.ASCII.GetString(receivedBytes);

            AppLogger.Information($"Received response on {broadcaster.InterfaceName} from {remoteIpEndPoint.Address}");

            if (IsValidDiscoveryMessage(receivedText))
            {
                var stream = new MemoryStream(receivedBytes);
                var message = DaqifiOutMessage.Parser.ParseDelimitedFrom(stream);
                var device = GetDeviceFromProtobufMessage(message);
                device.IpAddress = remoteIpEndPoint.Address.ToString();
                NotifyDeviceFound(this, device);
            }

            // Continue receiving on this broadcaster
            broadcaster.Client.BeginReceive(HandleFinderMessageReceived, broadcaster);
        }
        catch (ObjectDisposedException)
        {
            // Expected when stopping
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"Problem receiving on {broadcaster?.InterfaceName}: {ex.Message}");
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
            DeviceSerialNo = deviceSn.ToString(),
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
}