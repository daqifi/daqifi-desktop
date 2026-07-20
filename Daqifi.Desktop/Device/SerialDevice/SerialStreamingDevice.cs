using System.IO;
using System.IO.Ports;
using CommunityToolkit.Mvvm.ComponentModel;
using Daqifi.Core.Device;
using Daqifi.Core.Communication.Transport;
using Daqifi.Core.Communication.Messages;
using Daqifi.Core.Firmware;
using ScpiMessageProducer = Daqifi.Core.Communication.Producers.ScpiMessageProducer;
using CoreStreamingDevice = Daqifi.Core.Device.DaqifiStreamingDevice;

namespace Daqifi.Desktop.Device.SerialDevice;

public partial class SerialStreamingDevice : AbstractStreamingDevice, ILanChipInfoProvider
{
    #region Properties
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayIdentifier))]
    private SerialPort? _port;

    private SerialStreamTransport? _transport;

    /// <summary>
    /// Gets the actual COM port name for UART communication
    /// This is needed because Name may be set to the device part number after discovery
    /// </summary>
    public string PortName => Port?.PortName ?? Name;

    /// <summary>
    /// USB physical-location key resolved by Core discovery for this device's port, or null if it
    /// couldn't be resolved (non-Windows, or the port couldn't be matched to a PnP instance). Stable
    /// across serial ⇄ HID-bootloader mode transitions on the same physical device, so it can target an
    /// auto-update's reboot-triggered bootloader search without racing the device path (which only
    /// exists once the device has already re-enumerated into bootloader mode).
    /// </summary>
    public string? LocationKey { get; set; }

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
            case TimeoutException:
                // Core's InitializeAsync throws this when the device never reports its channel
                // configuration within its channel-population timeout. A newly-enumerated COM
                // port whose driver/firmware hasn't settled yet, or a device that's unplugged/
                // powered off/stuck in a non-responsive state, is a user/environmental condition,
                // not an app bug — same classification as the WiFi connect-timeout case (issue
                // #517, #632).
                AppLogger.Warning(ex, $"Device on {PortName} did not respond within the connection timeout");
                break;
            case InvalidOperationException when IsScpiInitializationError(ex):
                // Core's InitializeAsync throws a bare InvalidOperationException, message
                // "Device returned a SCPI error during initialization: ...", when any command in
                // its init sequence (echo/stop/power/stream-format/sysinfo, including
                // "SYSTem:STReam:INTerface 0" which sets the stream interface to USB) gets a SCPI
                // -200 execution error back. Firmware persists the last stream interface across
                // sessions, so a device previously left streaming over WiFi is a common trigger on
                // the very next USB connect, but any command in the sequence can hit this
                // transient/timing condition. Matched by message substring (Core doesn't yet throw
                // a typed exception for this — daqifi-core issue tracks that) so other
                // InvalidOperationException bugs still hit Error. Device/environmental condition,
                // not an app bug (issue #589).
                AppLogger.Warning(ex, $"Device on {PortName} returned a SCPI error during initialization");
                break;
            default:
                AppLogger.Error(ex, $"Failed to connect on {PortName}");
                break;
        }
    }

    /// <summary>
    /// True when <paramref name="ex"/> is Core's SCPI-error-during-initialization
    /// <see cref="InvalidOperationException"/> (issue #589). Matched on Core's full known prefix
    /// rather than the bare substring "SCPI error" so an unrelated InvalidOperationException that
    /// happens to mention a SCPI error elsewhere in its message isn't misclassified. Extracted as
    /// a pure predicate so the classification is unit-testable without exercising the logger.
    /// </summary>
    internal static bool IsScpiInitializationError(Exception ex) =>
        ex.Message.Contains("SCPI error during initialization", StringComparison.OrdinalIgnoreCase);

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
                AppLogger.Warning(ex, "Error disconnecting transport during cleanup");
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

    /// <summary>
    /// Powers on the WiFi module (<c>SYSTem:POWer:STATe 1</c>) so a subsequent chip-info query
    /// reaches a live module. After a PIC32 reboot the WiFi module comes back powered off, so the
    /// connect-time probe and the version check before a flash both call this first.
    /// </summary>
    public bool PowerOnWifiModule()
    {
        if (CoreDevice == null || !CoreDevice.IsConnected)
        {
            AppLogger.Warning($"Cannot power on WiFi module for {PortName}: core device is not connected.");
            return false;
        }

        try
        {
            AppLogger.Information($"Powering on WiFi module for {PortName}: SYSTem:POWer:STATe 1");
            CoreDevice.Send(ScpiMessageProducer.TurnDeviceOn);
            return true;
        }
        catch (Exception ex)
        {
            // A transport failure here shouldn't crash the probe/flash caller; report and let the
            // subsequent chip-info query surface the not-ready state.
            AppLogger.Warning(ex, $"Failed to power on WiFi module for {PortName}");
            return false;
        }
    }

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
        // The APPLY that triggers bridge-mode initialisation is sent later — via the raw serial
        // bridge-activation callback Core invokes — at the moment the flash tool shows
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
