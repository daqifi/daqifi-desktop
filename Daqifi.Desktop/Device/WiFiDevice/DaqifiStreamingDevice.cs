using System.Net.Sockets;
using Daqifi.Core.Communication.Messages;
using Daqifi.Core.Communication.Transport;
using Daqifi.Core.Device;
using CoreDeviceInfo = Daqifi.Core.Device.Discovery.IDeviceInfo;
using CoreStreamingDevice = Daqifi.Core.Device.DaqifiStreamingDevice;

namespace Daqifi.Desktop.Device.WiFiDevice;

/// <summary>
/// WiFi streaming device that uses Core's DaqifiStreamingDevice directly for communication.
/// Unlike USB devices, WiFi devices don't need consumer swapping because SD card operations
/// are only available over USB.
/// </summary>
public class DaqifiStreamingDevice : AbstractStreamingDevice
{
    #region Properties

    public int Port { get; set; }
    public bool IsPowerOn { get; set; }
    public override ConnectionType ConnectionType => ConnectionType.Wifi;

    #endregion

    #region Constructor
    /// <summary>
    /// Creates a WiFi device wrapper from Core discovery metadata.
    /// </summary>
    /// <param name="deviceInfo">The discovered device metadata.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="deviceInfo"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="deviceInfo"/> does not contain the IP address or TCP port required for WiFi.
    /// </exception>
    public DaqifiStreamingDevice(CoreDeviceInfo deviceInfo)
    {
        ArgumentNullException.ThrowIfNull(deviceInfo);

        if (deviceInfo.IPAddress == null)
        {
            throw new ArgumentException("WiFi device discovery info must include an IP address.", nameof(deviceInfo));
        }

        if (!deviceInfo.Port.HasValue)
        {
            throw new ArgumentException("WiFi device discovery info must include a TCP port.", nameof(deviceInfo));
        }

        Name = deviceInfo.Name;
        Metadata.SerialNumber = deviceInfo.SerialNumber;
        Metadata.IpAddress = deviceInfo.IPAddress.ToString();
        Metadata.MacAddress = deviceInfo.MacAddress ?? string.Empty;
        Metadata.FirmwareVersion = deviceInfo.FirmwareVersion;
        Port = deviceInfo.Port.Value;
        IsPowerOn = deviceInfo.IsPowerOn;
        IsStreaming = false;
    }

    #endregion

    #region Override Methods
    /// <summary>
    /// Connects a Core streaming device over TCP for the shared
    /// <see cref="AbstractStreamingDevice.Connect"/> template.
    /// </summary>
    protected override CoreStreamingDevice? CreateCoreDevice()
    {
        var options = new DeviceConnectionOptions
        {
            DeviceName = string.IsNullOrWhiteSpace(Name) ? "DAQiFi Device" : Name,
            ConnectionRetry = ConnectionRetryOptions.NoRetry,
            InitializeDevice = false
        };

        var connectedDevice = DaqifiDeviceFactory.ConnectTcp(IpAddress, Port, options);
        if (connectedDevice is not CoreStreamingDevice coreDevice)
        {
            connectedDevice.Dispose();
            throw new InvalidOperationException("Connected Core device does not support streaming operations.");
        }

        if (!coreDevice.IsConnected)
        {
            AppLogger.Error($"Failed to connect to DAQiFi device at {IpAddress}:{Port}");
            coreDevice.Dispose();
            return null;
        }

        return coreDevice;
    }

    protected override void LogConnectFailure(Exception ex)
    {
        switch (ex)
        {
            case OperationCanceledException:
            case TimeoutException:
                // Core's TcpStreamTransport enforces its connection timeout with a
                // CancellationTokenSource, so an unreachable device (powered off, stale
                // discovery entry, wrong subnet) surfaces as a TaskCanceledException —
                // or a TimeoutException once daqifi-core#237 translates it. Treat as a
                // user/environmental condition, not an app bug — log a warning (keeping
                // the exception detail in the local log) instead of capturing to Sentry
                // (issue #517).
                AppLogger.Warning(ex, $"Device at {IpAddress}:{Port} did not respond within the connection timeout");
                break;
            case SocketException socketEx:
                // Connection refused / host unreachable. Same classification as above.
                AppLogger.Warning(ex, $"Cannot reach device at {IpAddress}:{Port}: {socketEx.SocketErrorCode}");
                break;
            default:
                AppLogger.Error(ex, $"Problem with connecting to DAQiFi Device at {IpAddress}:{Port}");
                break;
        }
    }

    /// <summary>
    /// Not supported for WiFi devices. Commands are sent internally via the Core device.
    /// </summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override bool Write(string command)
    {
        throw new NotSupportedException(
            "Raw text writes are not supported on WiFi devices.");
    }

    /// <summary>
    /// Sends messages directly through Core's DaqifiDevice instead of using adapters.
    /// </summary>
    protected override void SendMessage(IOutboundMessage<string> message)
    {
        if (CoreDevice == null || !CoreDevice.IsConnected)
        {
            AppLogger.Warning(
                $"Cannot send message to {IpAddress}:{Port}: Core device is not connected " +
                $"(MessageType={message?.GetType().Name})");
            return;
        }

        try
        {
            CoreDevice.Send(message);
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                ex, $"Failed to send message to {IpAddress}:{Port} (MessageType={message?.GetType().Name})");
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
        if (obj is not DaqifiStreamingDevice other) { return false; }
        if (Name != other.Name) { return false; }
        if (IpAddress != other.IpAddress) { return false; }
        if (MacAddress != other.MacAddress) { return false; }
        return true;
    }
    #endregion
}
