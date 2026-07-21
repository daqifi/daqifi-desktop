using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Device.SerialDevice;
using Daqifi.Desktop.Helpers;
using Daqifi.Desktop.Logger;
using System.ComponentModel;
using System.IO.Ports;
using System.Management;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Daqifi.Desktop;

public partial class ConnectionManager : ObservableObject
{
    #region Private Variables
    private readonly ManagementEventWatcher _deviceRemovedWatcher;
    #endregion

    #region Properties
    [ObservableProperty]
    private DAQiFiConnectionStatus _connectionStatus = DAQiFiConnectionStatus.Disconnected;

    [ObservableProperty]
    private List<IStreamingDevice> _connectedDevices;

    [ObservableProperty]
    private bool _isDisconnected = true;

    [ObservableProperty]
    private bool _notifyConnection;

    /// <summary>
    /// Human-readable description of the most recent unexpected disconnect, set just before
    /// <see cref="NotifyConnection"/> flips to <c>true</c> so subscribers can build a message
    /// naming the device and the reason (issue #638).
    /// </summary>
    [ObservableProperty]
    private string _lastDisconnectReason = string.Empty;

    public string ConnectionStatusString { get; set; } = "Disconnected";

    /// <summary>
    /// Callback for handling duplicate device situations.
    /// Should return the user's choice on how to handle the duplicate.
    /// </summary>
    public Func<DuplicateDeviceCheckResult, DuplicateDeviceAction> DuplicateDeviceHandler { get; set; }

    /// <summary>
    /// Tracks the device currently undergoing firmware update. Non-null for the whole update (PIC32 +
    /// WiFi + the post-flash serial reconnect), so it doubles as the app-global "firmware update in
    /// progress" gate: while it is set, the connection dialog suspends its serial/WiFi discovery and
    /// <see cref="Connect"/> refuses USB connects, so nothing races Core's post-flash reconnect for the
    /// COM port (issue #738). Changing it raises <see cref="FirmwareUpdateInProgressChanged"/> so an
    /// already-open connection dialog can react immediately.
    /// </summary>
    public IStreamingDevice? DeviceBeingUpdated
    {
        get => _deviceBeingUpdated;
        set
        {
            if (ReferenceEquals(_deviceBeingUpdated, value)) { return; }

            var wasInProgress = _deviceBeingUpdated != null;
            _deviceBeingUpdated = value;

            // Only raise when the in-progress state actually flips (set from null / cleared to null),
            // not on a device-to-device change (which never happens today, but keeps the signal clean).
            if (wasInProgress != (value != null))
            {
                FirmwareUpdateInProgressChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private IStreamingDevice? _deviceBeingUpdated;

    /// <summary>
    /// True while a firmware update is in progress (<see cref="DeviceBeingUpdated"/> is set). The
    /// connection dialog gates its discovery on this, and <see cref="Connect"/> refuses USB connects
    /// while it is true, so a user-initiated connect or a discovery probe cannot steal the COM port
    /// during Core's post-flash serial reconnect window (issue #738).
    /// </summary>
    public bool IsFirmwareUpdateInProgress => _deviceBeingUpdated != null;

    /// <summary>
    /// Raised when <see cref="IsFirmwareUpdateInProgress"/> flips. The connection dialog subscribes so a
    /// dialog that is already open when a flash starts stops its serial/WiFi discovery immediately (and
    /// resumes it when the flash ends) — the push half of the coordination; the pull half is the guards
    /// in the dialog's <c>Start*Discovery</c> and in <see cref="Connect"/>.
    /// </summary>
    public event EventHandler? FirmwareUpdateInProgressChanged;

    #endregion

    partial void OnConnectionStatusChanged(DAQiFiConnectionStatus value)
    {
        UpdateStatusString();
        IsDisconnected = value != DAQiFiConnectionStatus.Connected;
    }

    #region Singleton Constructor / Initalization
    private static readonly ConnectionManager instance = new();

    private ConnectionManager()
    {
        ConnectedDevices = new List<IStreamingDevice>();

        try
        {
            // EventType 3 is Device Removal
            var deviceRemovedQuery = new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 3");

            _deviceRemovedWatcher = new ManagementEventWatcher(deviceRemovedQuery);
            _deviceRemovedWatcher.EventArrived += (sender, eventArgs) => CheckIfSerialDeviceWasRemoved();
            _deviceRemovedWatcher.Start();
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error(ex, "Failed to initialize ManagementEventWatcher: " + ex.Message);
        }

    }

    public static ConnectionManager Instance => instance;

    #endregion

    public async Task Connect(IStreamingDevice device)
    {
        try
        {
            // Never let a user-initiated USB connect (or a discovery-driven one) open the COM port
            // while a firmware update is running: the device being flashed re-enumerates its USB-CDC
            // port mid-update, and Core's own JumpingToApp step reconnects it directly (not through
            // this method). A competing open here would steal the port out from under that reconnect
            // and strand the update in a JumpingToApp timeout even though the flash succeeded — the
            // exact failure in issue #738. WiFi connects are unaffected (different device path).
            // Core's reconnect calls the Core device's Connect() directly, so this gate can't block it.
            if (IsFirmwareUpdateInProgress && device.ConnectionType == ConnectionType.Usb)
            {
                AppLogger.Instance.Warning(
                    $"Refusing to connect USB device {device.Name} while a firmware update is in progress " +
                    "(the device reconnects itself after the flash).");
                ConnectionStatus = DAQiFiConnectionStatus.Error;
                return;
            }

            ConnectionStatus = DAQiFiConnectionStatus.Connecting;

            // Check for duplicate device before connecting
            var duplicateResult = CheckForDuplicateDevice(device);
            if (duplicateResult.IsDuplicate)
            {
                if (DuplicateDeviceHandler != null)
                {
                    var action = DuplicateDeviceHandler(duplicateResult);
                    switch (action)
                    {
                        case DuplicateDeviceAction.KeepExisting:
                            ConnectionStatus = DAQiFiConnectionStatus.AlreadyConnected;
                            return;
                        case DuplicateDeviceAction.Cancel:
                            ConnectionStatus = DAQiFiConnectionStatus.Disconnected;
                            return;
                        case DuplicateDeviceAction.SwitchToNew:
                            // Disconnect the existing device and continue with connection
                            Disconnect(duplicateResult.ExistingDevice);
                            break;
                    }
                }
                else
                {
                    // No handler set, default behavior is to reject the duplicate
                    ConnectionStatus = duplicateResult.ExistingDevice != null ? DAQiFiConnectionStatus.AlreadyConnected : DAQiFiConnectionStatus.Error;
                    return;
                }
            }
            
            var isConnected = await Task.Run(() => device.Connect());
            if (!isConnected)
            {
                ConnectionStatus = DAQiFiConnectionStatus.Error;
                return;
            }
            
            // Check again after connection (in case serial number wasn't available before connect)
            var postConnectDuplicateResult = CheckForDuplicateDevice(device);
            if (postConnectDuplicateResult.IsDuplicate)
            {
                // Disconnect the device we just connected since it's a duplicate
                device.Disconnect();
                ConnectionStatus = postConnectDuplicateResult.ExistingDevice != null ? DAQiFiConnectionStatus.AlreadyConnected : DAQiFiConnectionStatus.Error;
                return;
            }
            
            ConnectedDevices.Add(device);
            device.ConnectionLost += OnDeviceConnectionLost;
            await Task.Delay(1000);
            OnPropertyChanged("ConnectedDevices");
            ConnectionStatus = DAQiFiConnectionStatus.Connected;

            var connectionType = device.ConnectionType == ConnectionType.Usb ? "usb" : "wifi";
            AppLogger.Instance.SetDeviceContext(
                device.DevicePartNumber,
                device.DeviceSerialNo,
                device.DeviceVersion,
                connectionType,
                device.DataChannels?.Count(c => c.IsActive) ?? 0);
            AppLogger.Instance.AddBreadcrumb("device", $"Device connected: {device.Name} (S/N: {device.DeviceSerialNo}) via {connectionType}");
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error(ex, "Failed to Connect in Connection");
            ConnectionStatus = DAQiFiConnectionStatus.Error;
        }
    }

    public void Disconnect(IStreamingDevice device)
    {
        var connectionType = device.ConnectionType == ConnectionType.Usb ? "usb" : "wifi";
        try
        {
            device.ConnectionLost -= OnDeviceConnectionLost;
            device.Disconnect();
            ConnectedDevices.Remove(device);
            OnPropertyChanged("ConnectedDevices");

            AppLogger.Instance.AddBreadcrumb("device", $"Device disconnected: {device.Name} (S/N: {device.DeviceSerialNo}) via {connectionType}");

            if (ConnectedDevices.Count == 0)
            {
                AppLogger.Instance.ClearDeviceContext();
            }
            else
            {
                var remaining = ConnectedDevices[^1];
                var remainingType = remaining.ConnectionType == ConnectionType.Usb ? "usb" : "wifi";
                AppLogger.Instance.SetDeviceContext(
                    remaining.DevicePartNumber,
                    remaining.DeviceSerialNo,
                    remaining.DeviceVersion,
                    remainingType,
                    remaining.DataChannels?.Count(c => c.IsActive) ?? 0);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Instance.AddBreadcrumb("device", $"Device disconnect failed: {device.Name} (S/N: {device.DeviceSerialNo}) via {connectionType}", Common.Loggers.BreadcrumbLevel.Error);
            AppLogger.Instance.Error(ex, "Failed in Disconnect");
        }
    }

    public void Reboot(IStreamingDevice device)
    {
        try
        {
            device.ConnectionLost -= OnDeviceConnectionLost;
            device.Reboot();
            ConnectedDevices.Remove(device);
            OnPropertyChanged("ConnectedDevices");
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error(ex, "Failed in Reboot");
        }
    }

    public void UpdateStatusString()
    {
        ConnectionStatusString = ConnectionStatus switch
        {
            DAQiFiConnectionStatus.Disconnected => "Disconnected",
            DAQiFiConnectionStatus.Connecting => "Connecting",
            DAQiFiConnectionStatus.Connected => "Connected",
            DAQiFiConnectionStatus.Error => "Error",
            DAQiFiConnectionStatus.AlreadyConnected => "AlreadyConnected",
            _ => "Error"
        };
    }

    /// <summary>
    /// Handles a device's <see cref="IDevice.ConnectionLost"/> event — Core detected a
    /// spontaneous transport drop (reboot, unplug, WiFi/TCP timeout, HID disconnect) that this
    /// class would otherwise never learn about (issue #638). Mirrors the existing
    /// <see cref="CheckIfSerialDeviceWasRemoved"/> teardown: unsubscribe the device's channels,
    /// tear the connection down via <see cref="Disconnect(IStreamingDevice)"/> (which always
    /// re-runs a fresh Core device + <c>InitializeAsync</c> on the next connect), and surface a
    /// notification naming the device and the reason.
    /// </summary>
    private void OnDeviceConnectionLost(object? sender, ConnectionLostEventArgs e)
    {
        if (sender is not IStreamingDevice device)
        {
            return;
        }

        UiThreadHelper.InvokeOnUiThread(() =>
        {
            // During a firmware update the flashing device's transport drop is EXPECTED — it reboots
            // into the HID bootloader and back into the application. Core's FirmwareUpdateService owns
            // reconnecting THIS very Core device at the JumpingToApp step. If the desktop tears it down
            // here, Disconnect() disposes the Core device and its serial transport out from under Core,
            // so Core's reconnect loop operates on a disposed device and can never succeed — the update
            // then times out in JumpingToApp even though the flash was written and verified (issue #738).
            // Leave the connection fully intact; DeviceBeingUpdated clears when the flash finishes, and a
            // genuinely-failed reconnect is reconciled by the next disconnect event or a user action.
            if (IsDeviceBeingUpdated(device))
            {
                return;
            }

            // Already torn down via another path (e.g. explicit user disconnect raced this event).
            if (!ConnectedDevices.Contains(device))
            {
                return;
            }

            foreach (var channel in device.DataChannels)
            {
                LoggingManager.Instance.Unsubscribe(channel);
            }

            Disconnect(device);

            LastDisconnectReason = $"{device.DeviceDisplayName} disconnected ({e.Reason}).";
            NotifyConnection = true;
        }, failureLogMessage: "Dispatcher unavailable while handling ConnectionLost; UI update dropped.");
    }

    /// <summary>
    /// True when <paramref name="device"/> is the device currently undergoing a firmware update. During
    /// an update the device's serial transport drops and re-enumerates as an expected part of the flash,
    /// and Core owns the reconnect — so the desktop's disconnect-detection paths must leave that device's
    /// connection untouched rather than disposing the Core device Core is reconnecting (issue #738).
    /// Matches by reference (the update is driven on the exact connected instance) with a name fallback.
    /// </summary>
    private bool IsDeviceBeingUpdated(IStreamingDevice device)
    {
        var updating = DeviceBeingUpdated;
        return updating != null
            && (ReferenceEquals(updating, device)
                || string.Equals(updating.Name, device.Name, StringComparison.Ordinal));
    }

    private void CheckIfSerialDeviceWasRemoved()
    {
        NotifyConnection = false;
        var bw = new BackgroundWorker();
        bw.DoWork += delegate
        {
            HashSet<string> availableSerialPorts;
            try
            {
                availableSerialPorts = new HashSet<string>(
                    SerialPort.GetPortNames(),
                    StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error(ex, "Failed to enumerate serial ports after device change.");
                return;
            }

            var devicesToRemove = ConnectedDevices
                .OfType<SerialStreamingDevice>()
                .Where(device =>
                    string.IsNullOrWhiteSpace(device.Port?.PortName) ||
                    !availableSerialPorts.Contains(device.Port.PortName))
                .Cast<IStreamingDevice>()
                .ToList();

            foreach (var serialDevice in devicesToRemove)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(delegate
                {
                    // The device being flashed drops off the COM port when it reboots mid-update — an
                    // expected part of the flash that Core reconnects itself. Tearing it down here would
                    // dispose the Core device out from under Core's JumpingToApp reconnect and time the
                    // update out (issue #738), so skip it entirely (same guard as OnDeviceConnectionLost).
                    if (IsDeviceBeingUpdated(serialDevice))
                    {
                        return;
                    }

                    foreach (var channel in serialDevice.DataChannels)
                    {
                        LoggingManager.Instance.Unsubscribe(channel);
                    }

                    Disconnect(serialDevice);

                    if (!NotifyConnection)
                    {
                        // Scoped to this device so a later notification never shows a stale
                        // reason string left over from a previous, unrelated disconnect.
                        LastDisconnectReason = $"{serialDevice.DeviceDisplayName} disconnected (port removed).";
                        NotifyConnection = true;
                    }
                });
            }
        };

        bw.RunWorkerAsync();
    }

    /// <summary>
    /// Checks if a device is already connected by comparing serial numbers.
    /// </summary>
    /// <param name="newDevice">The device to check for duplicates</param>
    /// <returns>A result indicating if the device is a duplicate and which existing device it matches</returns>
    private DuplicateDeviceCheckResult CheckForDuplicateDevice(IStreamingDevice newDevice)
    {
        // If device doesn't have a serial number, we can't check for duplicates reliably
        if (string.IsNullOrWhiteSpace(newDevice.DeviceSerialNo))
        {
            AppLogger.Instance.Information($"Device {newDevice.Name} has no serial number - cannot check for duplicates");
            return new DuplicateDeviceCheckResult { IsDuplicate = false };
        }

        // Check if any existing device has the same serial number
        var existingDevice = ConnectedDevices.FirstOrDefault(d => 
            !string.IsNullOrWhiteSpace(d.DeviceSerialNo) && 
            d.DeviceSerialNo.Equals(newDevice.DeviceSerialNo, StringComparison.OrdinalIgnoreCase));

        if (existingDevice != null)
        {
            var newDeviceInterface = newDevice.ConnectionType == ConnectionType.Usb ? "USB" : "WiFi";
            var existingDeviceInterface = existingDevice.ConnectionType == ConnectionType.Usb ? "USB" : "WiFi";
            
            AppLogger.Instance.Information($"Duplicate device detected: Device already connected via {existingDeviceInterface}, attempted to add via {newDeviceInterface}");
            
            return new DuplicateDeviceCheckResult 
            { 
                IsDuplicate = true, 
                ExistingDevice = existingDevice,
                NewDevice = newDevice,
                NewDeviceInterface = newDeviceInterface,
                ExistingDeviceInterface = existingDeviceInterface
            };
        }

        return new DuplicateDeviceCheckResult { IsDuplicate = false };
    }
}

/// <summary>
/// Result of checking for duplicate devices
/// </summary>
public class DuplicateDeviceCheckResult
{
    public bool IsDuplicate { get; set; }
    public IStreamingDevice ExistingDevice { get; set; }
    public IStreamingDevice NewDevice { get; set; }
    public string NewDeviceInterface { get; set; }
    public string ExistingDeviceInterface { get; set; }
}

/// <summary>
/// Actions that can be taken when a duplicate device is detected
/// </summary>
public enum DuplicateDeviceAction
{
    KeepExisting,
    SwitchToNew,
    Cancel
}

public enum DAQiFiConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    Error,
    AlreadyConnected
}
