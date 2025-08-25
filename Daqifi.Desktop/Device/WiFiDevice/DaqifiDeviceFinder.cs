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
/// Device finder that handles both old firmware (responds to port 30303)
/// and new firmware (responds to source port) with multi-interface support
/// </summary>
public class DaqifiDeviceFinder : AbstractMessageConsumer, IDeviceFinder
{
    #region Private Data

    private const string DaqifiFinderQuery = "DAQiFi?\r\n";
    private const string NativeFinderQuery = "Discovery: Who is out there?\r\n";
    private const string PowerEvent = "Power event occurred";
    private readonly byte[] _queryCommandBytes = Encoding.ASCII.GetBytes(DaqifiFinderQuery);
    private readonly int _broadcastPort;
    private readonly object _deviceLock = new object();
    private readonly HashSet<string> _discoveredDevices = new();

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
    private UdpClient _legacyReceiver; // For firmware that responds to port 30303 by design

    #endregion

    #region Events

    public event OnDeviceFoundHandler OnDeviceFound;
    public event OnDeviceRemovedHandler OnDeviceRemoved;

    #endregion

    #region Constructor

    public DaqifiDeviceFinder(int broadcastPort)
    {
        _broadcastPort = broadcastPort;
        try
        {
            InitializeBroadcasters();
            
            // Create receiver on port 30303 for firmware that responds to this port by design
            try
            {
                _legacyReceiver = new UdpClient(_broadcastPort);
                _legacyReceiver.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                AppLogger.Information($"Receiver listening on port {_broadcastPort} for firmware responses");
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Could not create receiver on port {_broadcastPort}: {ex.Message}");
                // Continue without it - direct responses will still work
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Error in DaqifiDeviceFinder");
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
                // Set up receiving on each broadcaster's socket (for new firmware)
                foreach (var broadcaster in _broadcasters)
                {
                    broadcaster.Client.BeginReceive(HandleFinderMessageReceived, broadcaster);
                }
                
                // Set up receiving on legacy port (for old firmware)
                if (_legacyReceiver != null)
                {
                    _legacyReceiver.BeginReceive(HandleLegacyMessageReceived, null);
                }

                while (Running)
                {
                    // Create a copy to avoid collection modified exceptions
                    var currentBroadcasters = _broadcasters.ToList();
                    
                    foreach (var broadcaster in currentBroadcasters)
                    {
                        try
                        {
                            // Check if the client is still valid before sending
                            if (broadcaster?.Client?.Client != null && broadcaster.Client.Client.IsBound)
                            {
                                broadcaster.Client.Send(_queryCommandBytes, _queryCommandBytes.Length, broadcaster.BroadcastEndpoint);
                                
                                // Log only periodically to avoid spam
                                if (DateTime.Now.Second % 10 == 0)
                                {
                                    AppLogger.Information($"Broadcasting from {broadcaster.InterfaceName} ({broadcaster.InterfaceAddress}) to {broadcaster.BroadcastEndpoint}");
                                }
                            }
                        }
                        catch (SocketException sockEx)
                        {
                            AppLogger.Warning($"Error broadcasting from {broadcaster.InterfaceName}: {sockEx.Message}");
                            // Don't remove the broadcaster - it might recover
                        }
                        catch (ObjectDisposedException)
                        {
                            // Socket was disposed, skip this broadcaster
                            AppLogger.Warning($"Broadcaster for {broadcaster?.InterfaceName} was disposed");
                        }
                    }
                    Thread.Sleep(1000);
                }
            }
            else
            {
                AppLogger.Information("No suitable network interfaces found for broadcasting.");
                
                // Still listen on legacy port if available
                if (_legacyReceiver != null)
                {
                    _legacyReceiver.BeginReceive(HandleLegacyMessageReceived, null);
                }
                
                while (Running) { Thread.Sleep(5000); }
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
            
            // Close legacy receiver
            try
            {
                _legacyReceiver?.Close();
            }
            catch { }
            
            base.Stop();
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Error stopping device finder");
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
            
            ProcessDiscoveryResponse(receivedBytes, remoteIpEndPoint, broadcaster.InterfaceName, broadcaster.InterfaceAddress);
            
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
    
    private void HandleLegacyMessageReceived(IAsyncResult res)
    {
        try
        {
            var remoteIpEndPoint = new IPEndPoint(IPAddress.Any, 0);
            var receivedBytes = _legacyReceiver.EndReceive(res, ref remoteIpEndPoint);
            
            // For legacy receiver, we don't know the specific interface
            ProcessDiscoveryResponse(receivedBytes, remoteIpEndPoint, "Legacy Port 30303", null);
            
            // Continue receiving
            _legacyReceiver.BeginReceive(HandleLegacyMessageReceived, null);
        }
        catch (ObjectDisposedException)
        {
            // Expected when stopping
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"Problem receiving on legacy port: {ex.Message}");
        }
    }
    
    private void ProcessDiscoveryResponse(byte[] receivedBytes, IPEndPoint remoteEndPoint, string source, IPAddress localInterface)
    {
        var receivedText = Encoding.ASCII.GetString(receivedBytes);
        
        AppLogger.Information($"Received response on {source} from {remoteEndPoint.Address}:{remoteEndPoint.Port}");

        if (IsValidDiscoveryMessage(receivedText))
        {
            try
            {
                var stream = new MemoryStream(receivedBytes);
                var message = DaqifiOutMessage.Parser.ParseDelimitedFrom(stream);
                var device = GetDeviceFromProtobufMessage(message);
                device.IpAddress = remoteEndPoint.Address.ToString();
                
                // Store the local interface that received this response
                if (localInterface != null)
                {
                    device.LocalInterfaceAddress = localInterface.ToString();
                }
                
                // Prevent duplicate device notifications
                lock (_deviceLock)
                {
                    var deviceKey = $"{device.MacAddress}_{device.IpAddress}";
                    if (_discoveredDevices.Add(deviceKey))
                    {
                        NotifyDeviceFound(this, device);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Error parsing discovery response");
            }
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