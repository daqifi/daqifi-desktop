using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Helpers;
using Daqifi.Desktop.Logger;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CoreDeviceErrorEventArgs = Daqifi.Core.Device.DeviceErrorEventArgs;
using CoreSendFailedEventArgs = Daqifi.Core.Communication.Producers.MessageSendFailedEventArgs<string>;
using DeviceErrorSource = Daqifi.Core.Device.DeviceErrorSource;
using DeviceIdentity = Daqifi.Core.Device.DeviceIdentity;

namespace Daqifi.Desktop;

/// <summary>
/// Process-lifetime singleton owning the set of currently connected devices and the app-level
/// aggregate connection status the UI binds to.
/// </summary>
/// <remarks>
/// Responsibilities that are deliberately app-level policy rather than Core's:
/// <list type="bullet">
/// <item><description>
/// The aggregate <see cref="ConnectionStatus"/>. Core's <c>ConnectionStatus</c> is per-device;
/// this is the single status the shell renders.
/// </description></item>
/// <item><description>
/// Duplicate-device resolution across transports, and the <c>KeepExisting</c>/<c>SwitchToNew</c>
/// prompt the connection dialog renders. Matching itself delegates to Core's
/// <see cref="DeviceIdentity"/> (issue #752, stage 1).
/// </description></item>
/// <item><description>
/// The firmware-update carve-out: a device being flashed drops its transport as an expected part
/// of the flash, and Core owns reconnecting it, so this class must not tear it down (issue #738).
/// </description></item>
/// <item><description>
/// Log severity for Core's background-failure events (<see cref="IDevice.ErrorOccurred"/> and
/// <see cref="IDevice.SendFailed"/>). Whether a device failure is an app bug worth capturing to
/// Sentry or an environmental condition worth only a local Warning is app policy, not Core's —
/// see <see cref="IsAppBug"/> (issue #805).
/// </description></item>
/// </list>
/// <para>
/// Spontaneous transport drops arrive via <see cref="IDevice.ConnectionLost"/> and are handled by
/// <see cref="OnDeviceConnectionLost"/>. This class previously also ran a <c>Win32_DeviceChangeEvent</c>
/// WMI watcher to catch serial unplugs; Core 1.4.0 detects those itself, so the watcher was removed
/// (issue #752, stage 3).
/// </para>
/// </remarks>
public partial class ConnectionManager : ObservableObject
{
    #region Constants
    /// <summary>
    /// Longest SCPI verb written to the log when a send fails. Real verbs are far shorter; the cap
    /// only exists so a malformed payload cannot turn one failure into a wall of log.
    /// </summary>
    private const int MAX_LOGGED_COMMAND_LENGTH = 64;
    #endregion

    #region Properties
    [ObservableProperty]
    private DAQiFiConnectionStatus _connectionStatus = DAQiFiConnectionStatus.Disconnected;

    [ObservableProperty]
    private List<IStreamingDevice> _connectedDevices = [];

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
    public Func<DuplicateDeviceCheckResult, DuplicateDeviceAction>? DuplicateDeviceHandler { get; set; }

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

            var previous = _deviceBeingUpdated;
            var wasInProgress = _deviceBeingUpdated != null;
            _deviceBeingUpdated = value;

            // Only raise when the in-progress state actually flips (set from null / cleared to null),
            // not on a device-to-device change (which never happens today, but keeps the signal clean).
            if (wasInProgress != (value != null))
            {
                FirmwareUpdateInProgressChanged?.Invoke(this, EventArgs.Empty);
            }

            // When the update ends, reconcile the device whose teardown we suppressed during the flash.
            // On a successful flash Core reconnected it (IsConnected == true) and there's nothing to do;
            // but on a failed reconnect (e.g. a JumpingToApp timeout) it is still in ConnectedDevices with
            // a dead transport, so without this it would show as connected forever and block a clean
            // reconnect. Tear it down now, matching the normal disconnect path (issue #738 follow-up).
            if (previous != null && value == null)
            {
                ReconcileDeviceAfterUpdate(previous);
            }
        }
    }

    /// <summary>
    /// After a firmware update ends, tears down the just-updated device iff Core's post-flash reconnect
    /// left it disconnected. A successful update reconnects the device (no-op here); a failed one leaves
    /// a stale entry the desktop's teardown paths deliberately skipped during the flash, so reconcile it
    /// exactly as a normal disconnect would (unsubscribe channels, Disconnect, surface a reason).
    /// </summary>
    private void ReconcileDeviceAfterUpdate(IStreamingDevice device)
    {
        UiThreadHelper.InvokeOnUiThread(() =>
        {
            // Reconnected cleanly, or already removed by another path — nothing to reconcile.
            if (!ConnectedDevices.Contains(device) || device.IsConnected)
            {
                return;
            }

            foreach (var channel in device.DataChannels)
            {
                LoggingManager.Instance.Unsubscribe(channel);
            }

            Disconnect(device);

            LastDisconnectReason =
                $"{device.DeviceDisplayName} did not reconnect after the firmware update. " +
                "Power-cycle the device and reconnect.";
            NotifyConnection = true;
        }, failureLogMessage: "Dispatcher unavailable while reconciling device after firmware update.");
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
    }

    /// <summary>
    /// Test-only constructor. <see cref="Instance"/> is a process-wide singleton, so asserting the
    /// severity a background-failure report is logged at (issue #805) against the shared instance
    /// would have every other test class's logging land in the same mock. Tests build their own
    /// instance with their own sink instead.
    /// </summary>
    /// <param name="appLogger">The logging sink this instance reports through.</param>
    internal ConnectionManager(IAppLogger appLogger) : this()
    {
        AppLogger = appLogger;
    }

    public static ConnectionManager Instance => instance;

    /// <summary>
    /// Logging sink for this connection manager. Defaults to the process-wide
    /// <see cref="Common.Loggers.AppLogger.Instance"/>; typed as <see cref="IAppLogger"/> and
    /// settable only through the test constructor so the log level of a report can be asserted
    /// rather than merely that nothing threw.
    /// </summary>
    internal IAppLogger AppLogger { get; init; } = Common.Loggers.AppLogger.Instance;

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
                AppLogger.Warning(
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
                // Disconnect the device we just connected since it's a duplicate. It never made it
                // into ConnectedDevices, so Disconnect(device) does not apply here — but the port was
                // opened, so it must also be disposed or a rejected USB duplicate leaks its COM handle
                // for the process lifetime and blocks every later reconnect to that port.
                device.Disconnect();
                try
                {
                    (device as IDisposable)?.Dispose();
                }
                catch (Exception ex)
                {
                    // Exception-aware overload: keeps the stack trace in DAQiFiAppLog.log (where a
                    // leaked-handle report is diagnosed from) without escalating to Sentry.
                    AppLogger.Warning(
                        ex, $"Failed to dispose a rejected duplicate device ({device.Name}).");
                }
                ConnectionStatus = postConnectDuplicateResult.ExistingDevice != null ? DAQiFiConnectionStatus.AlreadyConnected : DAQiFiConnectionStatus.Error;
                return;
            }
            
            ConnectedDevices.Add(device);
            SubscribeDeviceEvents(device);
            await Task.Delay(1000);
            OnPropertyChanged(nameof(ConnectedDevices));
            ConnectionStatus = DAQiFiConnectionStatus.Connected;

            var connectionType = device.ConnectionType == ConnectionType.Usb ? "usb" : "wifi";
            AppLogger.SetDeviceContext(
                device.DevicePartNumber,
                device.DeviceSerialNo,
                device.DeviceVersion,
                connectionType,
                device.DataChannels?.Count(c => c.IsActive) ?? 0);
            AppLogger.AddBreadcrumb("device", $"Device connected: {device.Name} (S/N: {device.DeviceSerialNo}) via {connectionType}");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to Connect in Connection");
            ConnectionStatus = DAQiFiConnectionStatus.Error;
        }
    }

    public void Disconnect(IStreamingDevice device)
    {
        var connectionType = device.ConnectionType == ConnectionType.Usb ? "usb" : "wifi";
        try
        {
            UnsubscribeDeviceEvents(device);
            device.Disconnect();
            // Release any transport/port handle the device owns; SerialStreamingDevice.Dispose is
            // idempotent with the cleanup Disconnect already performed.
            (device as IDisposable)?.Dispose();
            ConnectedDevices.Remove(device);
            OnPropertyChanged(nameof(ConnectedDevices));

            AppLogger.AddBreadcrumb("device", $"Device disconnected: {device.Name} (S/N: {device.DeviceSerialNo}) via {connectionType}");

            if (ConnectedDevices.Count == 0)
            {
                AppLogger.ClearDeviceContext();
            }
            else
            {
                var remaining = ConnectedDevices[^1];
                var remainingType = remaining.ConnectionType == ConnectionType.Usb ? "usb" : "wifi";
                AppLogger.SetDeviceContext(
                    remaining.DevicePartNumber,
                    remaining.DeviceSerialNo,
                    remaining.DeviceVersion,
                    remainingType,
                    remaining.DataChannels?.Count(c => c.IsActive) ?? 0);
            }
        }
        catch (Exception ex)
        {
            AppLogger.AddBreadcrumb("device", $"Device disconnect failed: {device.Name} (S/N: {device.DeviceSerialNo}) via {connectionType}", Common.Loggers.BreadcrumbLevel.Error);
            AppLogger.Error(ex, "Failed in Disconnect");
        }
    }

    public void Reboot(IStreamingDevice device)
    {
        try
        {
            UnsubscribeDeviceEvents(device);
            device.Reboot();
            ConnectedDevices.Remove(device);
            OnPropertyChanged(nameof(ConnectedDevices));
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed in Reboot");
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

    #region Device Event Wiring
    /// <summary>
    /// Attaches every per-device event this class listens to. Kept as a single method with an exact
    /// mirror in <see cref="UnsubscribeDeviceEvents"/> so a newly wired event cannot be attached at
    /// connect and forgotten at teardown — the leak shape fixed in issue #795. Runs once the device
    /// has connected and been accepted into <see cref="ConnectedDevices"/>, which is also when the
    /// Core device behind these events exists.
    /// </summary>
    private void SubscribeDeviceEvents(IStreamingDevice device)
    {
        device.ConnectionLost += OnDeviceConnectionLost;
        device.ErrorOccurred += OnDeviceErrorOccurred;
        device.SendFailed += OnDeviceSendFailed;
    }

    /// <summary>
    /// Detaches everything <see cref="SubscribeDeviceEvents"/> attached. Called from both teardown
    /// paths (<see cref="Disconnect(IStreamingDevice)"/> and <see cref="Reboot"/>) before the
    /// device's own teardown runs, while the underlying Core device is still alive to detach from.
    /// </summary>
    private void UnsubscribeDeviceEvents(IStreamingDevice device)
    {
        device.ConnectionLost -= OnDeviceConnectionLost;
        device.ErrorOccurred -= OnDeviceErrorOccurred;
        device.SendFailed -= OnDeviceSendFailed;
    }

    /// <summary>
    /// Reports a failure Core caught on one of a device's background threads (issue #805). Before
    /// Core 1.4.0 these had nowhere to go, so a read loop that could not read and a decoder that
    /// could not decode both presented to the user as a device that had simply stopped sending.
    /// </summary>
    /// <remarks>
    /// Observability only: Core does not tear the connection down for these, and neither does this
    /// handler. A genuinely dead link arrives separately as <see cref="IDevice.ConnectionLost"/>,
    /// which is where teardown and the user-facing notification live; automatic recovery is issue
    /// #804. No dispatcher hop either — nothing here touches bound state, and Core raises from a
    /// background thread.
    /// </remarks>
    private void OnDeviceErrorOccurred(object? sender, CoreDeviceErrorEventArgs e)
    {
        // Core already collapses repeats per (source, exception type) and reports how many it
        // swallowed, so this reports the count instead of adding a second throttle on top.
        var suppressed = e.SuppressedCount > 0
            ? $"; {e.SuppressedCount} further like failure(s) suppressed by Core's throttle"
            : string.Empty;
        var message =
            $"Device {DescribeDevice(sender)} reported a background failure from {e.Source} " +
            $"({e.Error.GetType().Name}: {e.Error.Message}){suppressed}.";

        if (IsAppBug(e.Source))
        {
            AppLogger.Error(e.Error, message);
            return;
        }

        AppLogger.Warning(e.Error, message);
    }

    /// <summary>
    /// Decides whether a background device failure is an app bug (log at Error, which captures to
    /// Sentry) or an environmental condition (log at Warning, which does not).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Routing environmental conditions to Error has burned this app three times (#775, #779, #801):
    /// the noise buries real bugs and the volume tracks how often users unplug things. So every
    /// source Core actually raises today is a Warning:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <c>MessageConsumer</c> — a failed transport read, parse, or subscriber dispatch. The
    /// dominant cause by far is a link that is dying or gone, which Core independently escalates to
    /// <c>ConnectionStatus.Lost</c>; every unplug would otherwise file a Sentry event. Core does not
    /// separate the subscriber-dispatch subcase (which would be an app bug), so that one is
    /// knowingly under-reported here rather than paying for it with a flood — it is still written to
    /// DAQiFiAppLog.log with its stack trace.
    /// </description></item>
    /// <item><description>
    /// <c>StreamDecode</c> — one malformed streaming frame. Core drops the frame and the stream
    /// survives, so this is firmware or link noise, not an app fault.
    /// </description></item>
    /// <item><description>
    /// <c>Reconnect</c> — Core exhausted its reconnect attempts. Terminal, but the cause is a device
    /// that is unplugged, powered off, or off the network. The user already gets the
    /// <see cref="IDevice.ConnectionLost"/> teardown and its dialog.
    /// </description></item>
    /// </list>
    /// <para>
    /// <c>Unknown</c> is the exception, and it is deliberately the same call made for
    /// <c>SerialPortConnectFailure.Unknown</c> in #801: no Core 1.4.0 path raises it, so seeing one
    /// means Core hit a failure it could not classify — expected volume zero, and worth a look.
    /// A source value this build does not recognise is a different thing (the desktop is behind
    /// Core, not the device misbehaving) and stays a Warning.
    /// </para>
    /// </remarks>
    internal static bool IsAppBug(DeviceErrorSource source) => source switch
    {
        DeviceErrorSource.Unknown => true,
        DeviceErrorSource.MessageConsumer => false,
        DeviceErrorSource.StreamDecode => false,
        DeviceErrorSource.Reconnect => false,
        _ => false
    };

    /// <summary>
    /// Reports a command that never reached the device (issue #805). Sending is fire-and-forget, so
    /// before Core 1.4.0 a failed write was indistinguishable from a delivered one and the app's
    /// idea of device state could silently diverge from the device's.
    /// </summary>
    /// <remarks>
    /// Always a Warning: a write fails because the port closed, the device went away, or the device
    /// stopped draining its receive buffer (<c>IsTimeout</c>) — all conditions of the link, not app
    /// bugs. The distinction is still logged because "busy device" and "gone device" are diagnosed
    /// differently.
    /// </remarks>
    private void OnDeviceSendFailed(object? sender, CoreSendFailedEventArgs e)
    {
        var outcome = e.IsTimeout
            ? "timed out on the way to"
            : "failed to reach";
        AppLogger.Warning(
            e.Error,
            $"Command '{DescribeCommand(e.Message.Data)}' {outcome} device {DescribeDevice(sender)} " +
            $"and was not delivered ({e.Error.GetType().Name}: {e.Error.Message}).");
    }

    /// <summary>
    /// Names the device a background failure came from, the way the user sees it in the UI.
    /// </summary>
    private static string DescribeDevice(object? sender) =>
        sender is IStreamingDevice device ? device.DeviceDisplayName : "(unknown)";

    /// <summary>
    /// Reduces a SCPI command to its verb — everything before the first space — for logging.
    /// </summary>
    /// <remarks>
    /// Arguments are dropped deliberately: <c>SYSTem:COMMunicate:LAN:PASs "..."</c> carries the user's
    /// WiFi password in plaintext, and DAQiFiAppLog.log must never contain it. The verb alone
    /// answers the question a send failure raises — which command was lost.
    /// </remarks>
    private static string DescribeCommand(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return "(empty)";
        }

        var trimmed = data.Trim();
        var firstSpace = trimmed.IndexOf(' ');
        var verb = firstSpace < 0 ? trimmed : trimmed[..firstSpace];

        // A malformed or oversized payload must not turn one failure into a wall of log.
        return verb.Length <= MAX_LOGGED_COMMAND_LENGTH ? verb : verb[..MAX_LOGGED_COMMAND_LENGTH] + "...";
    }
    #endregion

    /// <summary>
    /// Handles a device's <see cref="IDevice.ConnectionLost"/> event — Core detected a
    /// spontaneous transport drop (reboot, unplug, WiFi/TCP timeout, HID disconnect) that this
    /// class would otherwise never learn about (issue #638). Unsubscribes the device's channels,
    /// tears the connection down via <see cref="Disconnect(IStreamingDevice)"/> (which always
    /// re-runs a fresh Core device + <c>InitializeAsync</c> on the next connect), and surfaces a
    /// notification naming the device and the reason.
    /// </summary>
    /// <remarks>
    /// This is the only unplug-detection path (issue #752, stage 3). It replaced a
    /// <c>Win32_DeviceChangeEvent</c> WMI watcher that re-enumerated
    /// <c>SerialPort.GetPortNames()</c> on every USB removal on the machine: Core 1.4.0
    /// (daqifi-core#382/#403) made <c>SerialStreamTransport</c> poll for its own port's continued
    /// presence and raise <c>ConnectionStatus.Lost</c>, which reaches here as a
    /// <see cref="ConnectionLostEventArgs"/>. Core requires two consecutive misses of a one-second
    /// poll, so a physically unplugged device is reported within roughly three seconds even with
    /// no traffic — the idle case the WMI watcher existed to cover — and the check is armed only
    /// when the port was visible to that probe at connect time, so it cannot report a false drop.
    /// The replacement is strictly wider: the watcher only ever tore down
    /// <c>SerialStreamingDevice</c>s, while this fires for every transport.
    /// </remarks>
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
    /// and Core owns the reconnect — so <see cref="OnDeviceConnectionLost"/> must leave that device's
    /// connection untouched rather than disposing the Core device Core is reconnecting (issue #738).
    /// This carve-out matters more since Core 1.4.0, not less: Core now reports the mid-flash drop
    /// itself, within about three seconds of the device leaving the bus (issue #752, stage 3).
    /// </summary>
    /// <remarks>
    /// The firmware flow always drives the exact connected instance, so reference equality is the primary
    /// (and normally sufficient) match. The fallback compares the serial number — a stable hardware
    /// identity — never the display <c>Name</c>, which is a mutable, non-unique user label and could make
    /// an unrelated same-named device's disconnect be wrongly skipped, leaving a zombie connection.
    /// </remarks>
    private bool IsDeviceBeingUpdated(IStreamingDevice device)
    {
        var updating = DeviceBeingUpdated;
        if (updating == null)
        {
            return false;
        }

        if (ReferenceEquals(updating, device))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(updating.DeviceSerialNo)
            && string.Equals(updating.DeviceSerialNo, device.DeviceSerialNo, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether <paramref name="newDevice"/> is the same physical unit as one already in
    /// <see cref="ConnectedDevices"/>, so the same device reached over USB and over WiFi is not
    /// connected twice.
    /// </summary>
    /// <remarks>
    /// Matching is delegated to Core's transport-independent <see cref="DeviceIdentity"/> (issue #752),
    /// which compares the serial number first and falls back to the MAC address when the serial number
    /// is blank — the WiFi path knows a device's MAC before its serial number arrives with the first
    /// status message, a case the previous serial-only comparison could not detect at all. A serial
    /// mismatch is decisive, so a weaker discriminator can never merge two genuinely different units,
    /// and devices carrying no discriminator at all still never match each other.
    /// </remarks>
    /// <param name="newDevice">The device to check for duplicates</param>
    /// <returns>A result indicating if the device is a duplicate and which existing device it matches</returns>
    private DuplicateDeviceCheckResult CheckForDuplicateDevice(IStreamingDevice newDevice)
    {
        var candidateIdentity = DeviceIdentity.Create(newDevice.DeviceSerialNo, newDevice.MacAddress);

        // With no discriminator at all there is nothing to compare against, so duplicates are undetectable.
        if (candidateIdentity.IsEmpty)
        {
            AppLogger.Information(
                $"Device {newDevice.Name} has no serial number or MAC address - cannot check for duplicates");
            return new DuplicateDeviceCheckResult { IsDuplicate = false };
        }

        var existingDevice = ConnectedDevices.FirstOrDefault(d =>
            DeviceIdentity.Create(d.DeviceSerialNo, d.MacAddress).Matches(candidateIdentity));

        if (existingDevice != null)
        {
            var newDeviceInterface = newDevice.ConnectionType == ConnectionType.Usb ? "USB" : "WiFi";
            var existingDeviceInterface = existingDevice.ConnectionType == ConnectionType.Usb ? "USB" : "WiFi";
            
            AppLogger.Information(
                $"Duplicate device detected ({candidateIdentity}): Device already connected via " +
                $"{existingDeviceInterface}, attempted to add via {newDeviceInterface}");
            
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
    /// <summary>
    /// True when the device being connected is already connected over another interface.
    /// Only then are <see cref="ExistingDevice"/> and <see cref="NewDevice"/> populated.
    /// </summary>
    [MemberNotNullWhen(true, nameof(ExistingDevice), nameof(NewDevice))]
    public bool IsDuplicate { get; set; }

    public IStreamingDevice? ExistingDevice { get; set; }
    public IStreamingDevice? NewDevice { get; set; }
    public string NewDeviceInterface { get; set; } = string.Empty;
    public string ExistingDeviceInterface { get; set; } = string.Empty;
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
