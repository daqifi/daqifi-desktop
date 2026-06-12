using System.IO;
using System.IO.Ports;
using Daqifi.Core.Device;
using Daqifi.Core.Communication.Transport;
using Daqifi.Core.Communication.Messages;
using Daqifi.Core.Firmware;
using ScpiMessageProducer = Daqifi.Core.Communication.Producers.ScpiMessageProducer;
using CoreStreamingDevice = Daqifi.Core.Device.DaqifiStreamingDevice;

namespace Daqifi.Desktop.Device.SerialDevice;

public class SerialStreamingDevice : AbstractStreamingDevice, ILanChipInfoProvider
{
    private static readonly TimeSpan InitialStatusTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan InitialStatusPollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan InitialStatusRequestInterval = TimeSpan.FromSeconds(1);

    #region Properties
    private SerialPort? _port;
    private SerialStreamTransport? _transport;
    private TaskCompletionSource<bool>? _initialStatusReceivedSource;

    public SerialPort? Port
    {
        get => _port;
        set
        {
            if (_port != value)
            {
                _port = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayIdentifier));
            }
        }
    }

    /// <summary>
    /// Gets the actual COM port name for UART communication
    /// This is needed because Name may be set to the device part number after discovery
    /// </summary>
    public string PortName => Port?.PortName ?? Name;

    public override ConnectionType ConnectionType => ConnectionType.Usb;

    protected override CoreStreamingDevice? CoreDeviceForSd => CoreDevice;
    #endregion

    #region Constructor
    public SerialStreamingDevice(string portName)
    {
        Name = portName;
        Port = new SerialPort(portName);
    }

    internal SerialStreamingDevice(string portName, CoreStreamingDevice coreDevice)
        : this(portName)
    {
        CoreDevice = coreDevice ?? throw new ArgumentNullException(nameof(coreDevice));
    }

    /// <summary>
    /// Creates a SerialStreamingDevice with device info from Core's discovery.
    /// Use this constructor when device has already been probed.
    /// </summary>
    public SerialStreamingDevice(string portName, string? deviceName, string? serialNumber, string? firmwareVersion)
    {
        Name = !string.IsNullOrWhiteSpace(deviceName) ? deviceName : portName;
        Port = new SerialPort(portName);
        Metadata.SerialNumber = serialNumber ?? string.Empty;
        Metadata.FirmwareVersion = firmwareVersion ?? string.Empty;
    }

    #endregion
    #region Override Methods
    /// <summary>
    /// Connects a Core streaming device over the serial transport for the shared
    /// <see cref="AbstractStreamingDevice.Connect"/> template. <see cref="CoreDevice"/> is
    /// assigned before its transport connect so every failure path is cleaned up by
    /// <see cref="CleanupConnection"/>.
    /// </summary>
    protected override CoreStreamingDevice CreateCoreDevice()
    {
        _initialStatusReceivedSource = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // Use Core's transport for unified message handling (both send and receive).
        // The transport manages the actual SerialPort connection internally; DTR must stay
        // enabled or the device will not stream over USB.
        _transport = new SerialStreamTransport(Port.PortName, enableDtr: true);
        _transport.Connect();

        CoreDevice = new CoreStreamingDevice(
            string.IsNullOrWhiteSpace(Name) ? "DAQiFi Serial Device" : Name,
            _transport);
        CoreDevice.Connect();
        return CoreDevice;
    }

    protected override void LogConnectFailure(Exception ex)
    {
        switch (ex)
        {
            case FileNotFoundException:
                // .NET's SerialPort.Open throws FileNotFoundException when the COM port no
                // longer exists (device unplugged, never present, or renamed). Treat as a
                // user/environmental condition, not an app bug — log a warning (keeping the
                // exception detail in the local log) instead of capturing to Sentry.
                AppLogger.Warning(ex, $"Cannot connect on {PortName}: port is not available");
                break;
            case UnauthorizedAccessException:
                // SerialPort.Open throws UnauthorizedAccessException when another process
                // already holds the port open. Same classification as above.
                AppLogger.Warning(ex, $"Cannot connect on {PortName}: port is in use by another process");
                break;
            default:
                AppLogger.Error(ex, $"Failed to connect on {PortName}");
                break;
        }
    }

    /// <summary>
    /// Signals the initial-status wait so <see cref="OnCoreDeviceInitialized"/> can return,
    /// then runs the shared Core-to-desktop sync.
    /// </summary>
    protected override void OnCoreChannelsPopulated(object? sender, ChannelsPopulatedEventArgs e)
    {
        _initialStatusReceivedSource?.TrySetResult(true);
        base.OnCoreChannelsPopulated(sender, e);
    }

    /// <summary>
    /// Blocks until the device reports its initial status message, which gates
    /// <see cref="AbstractStreamingDevice.Connect"/> returning for serial devices.
    /// </summary>
    protected override void OnCoreDeviceInitialized()
    {
        var coreDevice = CoreDevice;
        if (coreDevice == null)
        {
            throw new InvalidOperationException("Core device was not initialized.");
        }

        var statusReceivedSource = _initialStatusReceivedSource
            ?? throw new InvalidOperationException("Initial status wait source was not initialized.");

        var deadline = DateTime.UtcNow + InitialStatusTimeout;
        var nextDeviceInfoRequestAt = DateTime.UtcNow + InitialStatusRequestInterval;

        while (DateTime.UtcNow < deadline)
        {
            if (statusReceivedSource.Task.Wait(InitialStatusPollInterval))
            {
                return;
            }

            if (DateTime.UtcNow < nextDeviceInfoRequestAt)
            {
                continue;
            }

            try
            {
                coreDevice.Send(ScpiMessageProducer.GetDeviceInfo);
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Failed to re-request device info on {PortName}: {ex.Message}");
            }

            nextDeviceInfoRequestAt = DateTime.UtcNow + InitialStatusRequestInterval;
        }

        throw new TimeoutException(
            $"Device on {PortName} did not report status within {InitialStatusTimeout.TotalSeconds:F0} seconds of connect.");
    }

    /// <summary>
    /// Sends a message to the device using Core's DaqifiDevice.
    /// </summary>
    protected override void SendMessage(IOutboundMessage<string> message)
    {
        if (CoreDevice == null || !CoreDevice.IsConnected)
        {
            AppLogger.Warning($"Cannot send to {PortName}: Core device not connected");
            return;
        }
        CoreDevice.Send(message);
    }

    public override bool Write(string command)
    {
        try
        {
            // Use transport's stream for raw writes when available
            if (_transport?.IsConnected == true)
            {
                var bytes = System.Text.Encoding.ASCII.GetBytes(command);
                _transport.Stream.Write(bytes, 0, bytes.Length);
                return true;
            }

            // Fallback to Port for legacy/discovery scenarios
            if (Port?.IsOpen == true)
            {
                Port.WriteTimeout = 1000;
                Port.Write(command);
                return true;
            }

            AppLogger.Warning($"Cannot write to {PortName}: no connection available");
            return false;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to write in SerialStreamingDevice");
            return false;
        }
    }

    protected override void CleanupConnection()
    {
        _initialStatusReceivedSource = null;

        // Unsubscribe Core device events and dispose the Core device first
        base.CleanupConnection();

        // Clean up transport (this handles the actual serial port)
        if (_transport != null)
        {
            try
            {
                _transport.Disconnect();
                _transport.Dispose();
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Error disconnecting transport during cleanup: {ex.Message}");
            }
            _transport = null;
        }

        // Note: Port cleanup is now handled by transport
    }

    #endregion

    #region Serial Device Only Methods
    public void SendScpiMessage(IOutboundMessage<string> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        SendMessage(message);
    }

    /// <summary>
    /// Gets the connected Core serial streaming device used for firmware update workflows.
    /// </summary>
    internal CoreStreamingDevice ConnectedCoreStreamingDevice => CoreDevice ?? throw new InvalidOperationException(
        $"Core streaming device for {PortName} is not connected.");

    public bool EnableLanUpdateMode()
    {
        AppLogger.Information($"Preparing {PortName} for WiFi firmware mode.");
        if (CoreDevice == null || !CoreDevice.IsConnected)
        {
            AppLogger.Warning($"Cannot prepare {PortName} for WiFi firmware mode: core device is not connected.");
            return false;
        }

        // Power on the WiFi module and set the FW-update-requested flag.
        // APPLY (SYSTem:COMMunicate:LAN:APPLY) is intentionally NOT sent here.
        // The APPLY that triggers bridge-mode initialisation is sent later — via a raw serial
        // port write inside WifiPromptDelayProcessRunner — at the moment the flash tool shows
        // its "Power cycle WINC" prompt.  Sending APPLY here (before the flash tool starts)
        // introduces a race: the WiFi deinit/reinit cycle can take several seconds, and the
        // bridge may not be ready by the time the flash tool's programming phase begins.
        // Sending it at the prompt gives the firmware a guaranteed ~2 s window to initialise
        // the bridge before the tool issues its first serial query.
        Thread.Sleep(2000);
        AppLogger.Information("Sending LAN FW update prep command: SYSTem:POWer:STATe 1");
        CoreDevice.Send(ScpiMessageProducer.TurnDeviceOn);
        Thread.Sleep(1000);
        AppLogger.Information("Sending LAN FW update prep command: SYSTem:COMMUnicate:LAN:FWUpdate");
        CoreDevice.Send(ScpiMessageProducer.SetLanFirmwareUpdateMode);
        return true;
    }

    public void ResetLanAfterUpdate()
    {
        CoreDevice?.Send(ScpiMessageProducer.SetUsbTransparencyMode(0));
        CoreDevice?.Send(ScpiMessageProducer.EnableNetworkLan);
        CoreDevice?.Send(ScpiMessageProducer.ApplyNetworkLan);
        CoreDevice?.Send(ScpiMessageProducer.SaveNetworkLan);
    }

    /// <summary>
    /// Queries the WiFi module chip information by delegating to the underlying Core device.
    /// </summary>
    public Task<LanChipInfo?> GetLanChipInfoAsync(CancellationToken cancellationToken = default)
    {
        if (CoreDevice is not ILanChipInfoProvider provider)
            return Task.FromResult<LanChipInfo?>(null);
        return provider.GetLanChipInfoAsync(cancellationToken);
    }

    /// <summary>
    /// Returns the COM port name for this USB device
    /// </summary>
    protected override string GetUsbDisplayIdentifier()
    {
        try
        {
            var name = Port?.PortName;
            return string.IsNullOrWhiteSpace(name) ? "USB" : name;
        }
        catch
        {
            return "USB";
        }
    }
    #endregion
}
