using Daqifi.Desktop.Device;
using Daqifi.Desktop.Device.Firmware;
using Daqifi.Desktop.Device.SerialDevice;
using Daqifi.Desktop.Device.WiFiDevice;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.View;
using System.Collections;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Daqifi.Core.Device.Discovery;
using CoreConcreteDeviceInfo = Daqifi.Core.Device.Discovery.DeviceInfo;
using CoreConnectionType = Daqifi.Core.Device.Discovery.ConnectionType;
using CoreDeviceInfo = Daqifi.Core.Device.Discovery.IDeviceInfo;

namespace Daqifi.Desktop.ViewModels;

public partial class ConnectionDialogViewModel : ObservableObject
{
    #region Private Variables
    private WiFiDeviceFinder? _wifiFinder;
    private Daqifi.Core.Device.Discovery.SerialDeviceFinder? _serialFinder;
    private CancellationTokenSource? _wifiDiscoveryCts;
    private CancellationTokenSource? _serialDiscoveryCts;
    private Task? _wifiDiscoveryTask;
    private Task? _serialDiscoveryTask;
    private readonly IDialogService _dialogService;

    /// <summary>
    /// App-global watcher that discovers and holds every sitting HID bootloader (keep-alive read pending)
    /// so Windows USB selective-suspend can't wedge one before the user flashes (daqifi-nyquist-firmware#568).
    /// The dialog binds its firmware list to <see cref="IBootloaderWatcher.Bootloaders"/> and opens the
    /// firmware dialog for the chosen device. Null only in unit tests that construct the view model
    /// without the DI container.
    /// </summary>
    private readonly IBootloaderWatcher? _watcher;

    [ObservableProperty]
    private bool _hasNoWiFiDevices = true;

    [ObservableProperty]
    private bool _hasNoSerialDevices = true;

    [ObservableProperty]
    private bool _hasNoHidDevices = true;

    private bool _closed;
    #endregion

    #region Events
    /// <summary>
    /// Raised when a connect command has completed successfully and the dialog should close.
    /// The view subscribes and closes itself. Not raised on empty input or failed connect.
    /// </summary>
    public event EventHandler? CloseRequested;
    #endregion

    #region Properties
    public ObservableCollection<DaqifiStreamingDevice> AvailableWiFiDevices { get; } = [];
    public ObservableCollection<SerialStreamingDevice> AvailableSerialDevices { get; } = [];

    /// <summary>
    /// The sitting HID bootloaders the app-global watcher is holding, bound to the Firmware tab. The user
    /// picks one to flash. Falls back to an empty collection when no watcher is present (unit tests).
    /// </summary>
    public ReadOnlyObservableCollection<HeldBootloader> AvailableHidDevices => _watcher?.Bootloaders ?? _emptyHidDevices;

    private readonly ReadOnlyObservableCollection<HeldBootloader> _emptyHidDevices =
        new(new ObservableCollection<HeldBootloader>());

    [ObservableProperty]
    private string? _manualPortName;

    /// <summary>
    /// User-facing validation message for the Manual USB tab. Non-null when the entered
    /// COM port failed pre-flight validation (e.g. port not present on the system).
    /// Cleared automatically when the user edits <see cref="ManualPortName"/>.
    /// </summary>
    [ObservableProperty]
    private string? _manualPortError;

    public SerialStreamingDevice ManualSerialDevice { get; set; }

    [ObservableProperty]
    private string? _manualIpAddress;

    /// <summary>
    /// User-facing validation message for the Manual WiFi tab. Non-null when the entered
    /// endpoint failed to resolve or the device did not respond (issue #517).
    /// Cleared automatically when the user edits <see cref="ManualIpAddress"/>.
    /// </summary>
    [ObservableProperty]
    private string? _manualWifiError;
    #endregion

    partial void OnManualPortNameChanged(string? value)
    {
        // Clear stale validation error as soon as the user starts editing the port name.
        if (!string.IsNullOrEmpty(ManualPortError))
        {
            ManualPortError = null;
        }
    }

    partial void OnManualIpAddressChanged(string? value)
    {
        // Clear stale validation error as soon as the user starts editing the address.
        if (!string.IsNullOrEmpty(ManualWifiError))
        {
            ManualWifiError = null;
        }
    }

    #region Constructor
    /// <summary>Creates the connection dialog view model using services resolved from the app container.</summary>
    public ConnectionDialogViewModel()
        : this(ServiceLocator.Resolve<IDialogService>(), App.ServiceProvider?.GetService<IBootloaderWatcher>()) { }

    /// <summary>Creates the connection dialog view model.</summary>
    /// <param name="dialogService">Dialog service used to display modal dialogs.</param>
    /// <param name="watcher">Optional app-global bootloader watcher (null in unit tests without the DI container).</param>
    public ConnectionDialogViewModel(
        IDialogService dialogService,
        IBootloaderWatcher? watcher = null)
    {
        _dialogService = dialogService;
        _watcher = watcher;
        ConnectCommand = new AsyncRelayCommand<object>(ConnectAsync);
        ConnectSerialCommand = new AsyncRelayCommand<object>(ConnectSerialAsync);
        ConnectManualSerialCommand = new AsyncRelayCommand(ConnectManualSerialAsync);
        ConnectManualWifiCommand = new AsyncRelayCommand(ConnectManualWifiAsync);

        // The watcher holds bootloaders app-wide, so its list may already be populated before the dialog
        // opens. Reflect its current count and track changes so the "scanning…" overlay is accurate.
        // (Bootloaders is a ReadOnlyObservableCollection — its CollectionChanged is an explicit interface
        // member, so reach it via INotifyCollectionChanged.)
        if (_watcher != null)
        {
            HasNoHidDevices = _watcher.Bootloaders.Count == 0;
            ((System.Collections.Specialized.INotifyCollectionChanged)_watcher.Bootloaders).CollectionChanged
                += OnHidDevicesChanged;
        }

        // Set up the duplicate device handler
        ConnectionManager.Instance.DuplicateDeviceHandler = HandleDuplicateDevice;
    }

    private void OnHidDevicesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        HasNoHidDevices = AvailableHidDevices.Count == 0;
    }

    public void StartConnectionFinders()
    {
        Common.Loggers.AppLogger.Instance.AddBreadcrumb("discovery", "Device discovery started");

        StartWiFiDiscovery();
        StartSerialDiscovery();
        // HID bootloader discovery + holding is the app-global watcher's job (started at app startup),
        // not the dialog's — the dialog only binds to AvailableHidDevices.
    }

    private void StartWiFiDiscovery()
    {
        // Idempotent while actually running; allow a restart once the prior loop has completed (e.g.
        // it was drained around a firmware flash), otherwise a stale task reference blocks discovery.
        if (_closed || _wifiDiscoveryTask is { IsCompleted: false }) { return; }

        // Restart-after-drain: dispose the prior finder/CTS before replacing them so we never leak a
        // subscribed finder or an undisposed CancellationTokenSource.
        if (_wifiFinder != null) { _wifiFinder.DeviceDiscovered -= HandleCoreWifiDeviceDiscovered; _wifiFinder.Dispose(); }
        _wifiDiscoveryCts?.Dispose();

        _wifiFinder = new WiFiDeviceFinder(30303);
        _wifiDiscoveryCts = new CancellationTokenSource();
        _wifiFinder.DeviceDiscovered += HandleCoreWifiDeviceDiscovered;
        _wifiDiscoveryTask = RunContinuousWiFiDiscoveryAsync(_wifiDiscoveryCts.Token);
    }

    private void StartSerialDiscovery()
    {
        if (_closed || _serialDiscoveryTask is { IsCompleted: false }) { return; }

        if (_serialFinder != null) { _serialFinder.DeviceDiscovered -= HandleCoreSerialDeviceDiscovered; _serialFinder.Dispose(); }
        _serialDiscoveryCts?.Dispose();

        _serialFinder = new Daqifi.Core.Device.Discovery.SerialDeviceFinder();
        _serialDiscoveryCts = new CancellationTokenSource();
        _serialFinder.DeviceDiscovered += HandleCoreSerialDeviceDiscovered;
        _serialDiscoveryTask = RunContinuousSerialDiscoveryAsync(_serialDiscoveryCts.Token);
    }

    private async Task RunContinuousWiFiDiscoveryAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _wifiFinder != null)
            {
                await _wifiFinder.DiscoverAsync(cancellationToken);
                // Brief pause before next discovery cycle
                await Task.Delay(3000, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancelled
        }
        catch (ObjectDisposedException)
        {
            // Expected when finder is disposed during discovery
        }
        catch (Exception ex)
        {
            Common.Loggers.AppLogger.Instance.Error(ex, "Error in WiFi discovery loop");
        }
    }

    private async Task RunContinuousSerialDiscoveryAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _serialFinder != null)
            {
                await _serialFinder.DiscoverAsync(cancellationToken);
                // Serial discovery is quick, pause longer between scans
                await Task.Delay(2000, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancelled
        }
        catch (ObjectDisposedException)
        {
            // Expected when finder is disposed during discovery
        }
        catch (Exception ex)
        {
            Common.Loggers.AppLogger.Instance.Error(ex, "Error in Serial discovery loop");
        }
    }

    #endregion

    #region Commands

    public IAsyncRelayCommand ConnectCommand { get; }
    public IAsyncRelayCommand ConnectSerialCommand { get; }
    public IAsyncRelayCommand ConnectManualSerialCommand { get; }
    public IAsyncRelayCommand ConnectManualWifiCommand { get; }

    private async Task ConnectAsync(object? selectedItems)
    {
        var selectedDevices = ToStreamingDevices(selectedItems);
        if (selectedDevices.Count == 0) { return; }

        StopWiFiDiscovery();

        foreach (var device in selectedDevices)
        {
            await ConnectionManager.Instance.Connect(device);
        }

        RaiseCloseRequested();
    }

    private async Task ConnectSerialAsync(object? selectedItems)
    {
        var selectedDevices = ToStreamingDevices(selectedItems);
        if (selectedDevices.Count == 0) { return; }

        await StopSerialDiscoveryAsync();

        foreach (var device in selectedDevices)
        {
            await ConnectionManager.Instance.Connect(device);
        }

        RaiseCloseRequested();
    }

    private async Task ConnectManualSerialAsync()
    {
        ManualPortError = null;

        if (string.IsNullOrWhiteSpace(ManualPortName)) { return; }

        var portName = ManualPortName.Trim();

        // Port enumeration reads the registry; keep it off the UI thread per the
        // app's async/UI-responsiveness standards (this runs before the first await).
        if (!await Task.Run(() => IsPortAvailable(portName)))
        {
            // Avoid the FileNotFoundException round-trip from SerialPort.Open by
            // pre-checking against the system's enumerated ports. Surface a friendly
            // message in the dialog instead of silently closing.
            ManualPortError =
                $"Port '{portName}' is not available. " +
                "Plug in the device or check Device Manager for the correct port name.";
            Common.Loggers.AppLogger.Instance.Warning(
                $"Manual serial connect rejected: port '{portName}' is not present on the system.");
            return;
        }

        // Drain serial discovery before opening the port — same exclusive-access guarantee the
        // discovered-device path (ConnectSerialAsync) already takes. The continuous SerialDeviceFinder
        // loop opens/probes every DAQiFi-VID/PID COM port each cycle (and a probe on a port that never
        // answers the identify sits there holding the handle), so without this the app races ITSELF for
        // the port and the open fails "in use by another process". This is the common path for a device
        // stranded in transparent mode: it isn't auto-discovered, so the user connects to it by port
        // here while discovery is still hammering it.
        await StopSerialDiscoveryAsync();

        ManualSerialDevice = new SerialStreamingDevice(portName);
        await ConnectionManager.Instance.Connect(ManualSerialDevice);

        // Post-connect status check covers failures the pre-flight enumeration cannot
        // catch — most notably "port exists but is held by another process", which
        // SerialStreamingDevice.Connect now classifies as a Warning (no Sentry capture).
        // Without this, the dialog would close silently and the user would have no idea
        // the connection failed.
        if (ConnectionManager.Instance.ConnectionStatus == DAQiFiConnectionStatus.Error)
        {
            ManualPortError =
                $"Could not connect to '{portName}'. " +
                "The port may be in use by another application or the device is not responding.";
            // Restart discovery so the dialog keeps finding devices after a failed manual attempt.
            StartSerialDiscovery();
            return;
        }

        RaiseCloseRequested();
    }

    private static bool IsPortAvailable(string portName)
    {
        try
        {
            return SerialPort.GetPortNames()
                .Any(p => string.Equals(p, portName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            // If port enumeration itself fails, fall back to the connect attempt rather
            // than blocking the user — Connect() will surface its own error path.
            Common.Loggers.AppLogger.Instance.Warning(ex,
                "Failed to enumerate serial ports during manual-connect validation.");
            return true;
        }
    }

    private async Task ConnectManualWifiAsync()
    {
        ManualWifiError = null;

        if (string.IsNullOrWhiteSpace(ManualIpAddress)) { return; }

        var endpointInput = ManualIpAddress.Trim();
        IPAddress? ipAddress;
        try
        {
            ipAddress = await ResolveManualWifiEndpointAsync(endpointInput);
        }
        catch (ArgumentException ex)
        {
            ManualWifiError = $"'{endpointInput}' is not a valid IP address or host name.";
            Common.Loggers.AppLogger.Instance.Warning(ex,
                $"Manual WiFi connection requires a valid IP address or host name. " +
                $"Received '{ManualIpAddress}'");
            return;
        }
        catch (SocketException ex)
        {
            ManualWifiError = $"Could not resolve '{endpointInput}'. Check the address and try again.";
            Common.Loggers.AppLogger.Instance.Warning(ex,
                $"Failed to resolve manual WiFi endpoint '{ManualIpAddress}'");
            return;
        }
        catch (Exception ex)
        {
            Common.Loggers.AppLogger.Instance.Error(
                ex,
                $"Unexpected error while resolving manual WiFi endpoint '{ManualIpAddress}'.");
            throw;
        }

        if (ipAddress == null)
        {
            ManualWifiError = $"Could not resolve '{endpointInput}'. Check the address and try again.";
            Common.Loggers.AppLogger.Instance.Warning(
                $"Manual WiFi endpoint '{ManualIpAddress}' did not resolve to an IP address.");
            return;
        }

        var deviceInfo = new CoreConcreteDeviceInfo
        {
            Name = "Manual IP Device",
            IPAddress = ipAddress,
            Port = 9760, // Common DAQiFi TCP data port - TODO: make configurable or discover dynamically
            IsPowerOn = true,
            ConnectionType = CoreConnectionType.WiFi
        };

        var device = new DaqifiStreamingDevice(deviceInfo);
        await ConnectionManager.Instance.Connect(device);

        // Post-connect status check mirrors the manual-serial path: an unreachable device
        // (connect timeout — issue #517) must keep the dialog open with an inline message
        // instead of closing silently.
        if (ConnectionManager.Instance.ConnectionStatus == DAQiFiConnectionStatus.Error)
        {
            ManualWifiError =
                $"Could not connect to '{endpointInput}'. " +
                "Verify the device is powered on and reachable on this network.";
            return;
        }

        RaiseCloseRequested();
    }

    private static List<IStreamingDevice> ToStreamingDevices(object? selectedItems)
    {
        if (selectedItems is not IEnumerable enumerable) { return []; }
        return enumerable.Cast<IStreamingDevice>().ToList();
    }

    private void RaiseCloseRequested()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private static async Task<IPAddress?> ResolveManualWifiEndpointAsync(string endpointInput)
    {
        if (IPAddress.TryParse(endpointInput, out var parsedIpAddress))
        {
            return parsedIpAddress;
        }

        var resolvedAddresses = await Dns.GetHostAddressesAsync(endpointInput);
        return resolvedAddresses.FirstOrDefault(
            address => address.AddressFamily == AddressFamily.InterNetwork)
            ?? resolvedAddresses.FirstOrDefault();
    }

    [RelayCommand]
    private async Task ConnectHid(object selectedItems)
    {
        if (selectedItems is not IEnumerable enumerable) { return; }

        var bootloader = enumerable.Cast<HeldBootloader>().FirstOrDefault();
        if (bootloader == null) { return; }

        // Pause WiFi + serial discovery while the firmware dialog is open: their per-cycle bus probing
        // (serial opens/probes every COM port; WiFi UDP-broadcasts) can starve the bootloader's HID I/O
        // mid-flash. HID discovery and the per-device holds belong to the app-global watcher — it pauses
        // HID discovery and releases the target's hold itself when the flash starts
        // (FirmwareDialogViewModel → BootloaderWatcher.PrepareFlashAsync), while every OTHER held
        // bootloader stays wedge-proof. Awaiting the drains matters: cancel/dispose does NOT abort an
        // in-flight DiscoverAsync cycle, so a still-running probe could otherwise hold a handle when the
        // flasher fires.
        await StopWiFiDiscoveryAsync();
        await StopSerialDiscoveryAsync();

        try
        {
            var firmwareDialogViewModel = new FirmwareDialogViewModel(bootloader.DisplayName, bootloader.DevicePath);
            _dialogService.ShowDialog<FirmwareDialog>(this, firmwareDialogViewModel);
        }
        finally
        {
            // Resume discovery once the flash dialog closes so the device list stays live. The watcher
            // keeps holding the bootloader (or drops it if the flash succeeded) on its own.
            StartWiFiDiscovery();
            StartSerialDiscovery();
        }
    }
    #endregion

    #region Core Device Discovery Event Handlers

    internal void HandleCoreWifiDeviceDiscovered(object? sender, DeviceDiscoveredEventArgs e)
    {
        try
        {
            Common.Loggers.AppLogger.Instance.AddBreadcrumb("discovery", $"WiFi device found: {e.DeviceInfo.Name}");
            var wifiDevice = new DaqifiStreamingDevice(e.DeviceInfo);
            HandleWifiDeviceFound(sender, wifiDevice);
        }
        catch (Exception ex)
        {
            Common.Loggers.AppLogger.Instance.Error(ex, "Error handling WiFi device discovery");
        }
    }

    private void HandleCoreSerialDeviceDiscovered(object? sender, DeviceDiscoveredEventArgs e)
    {
        try
        {
            Common.Loggers.AppLogger.Instance.AddBreadcrumb("discovery", $"Serial device found: {e.DeviceInfo.Name}");
            AddSerialDeviceFromDiscovery(e.DeviceInfo);
        }
        catch (Exception ex)
        {
            Common.Loggers.AppLogger.Instance.Error(ex, "Error handling Serial device discovery");
        }
    }

    private void AddSerialDeviceFromDiscovery(CoreDeviceInfo deviceInfo)
    {
        var portName = deviceInfo.PortName?.Trim();
        if (string.IsNullOrWhiteSpace(portName))
        {
            return;
        }

        InvokeOnUiThread(() =>
        {
            var existing = FindSerialDeviceByPortName(portName);
            if (existing == null)
            {
                var serialDevice = new SerialStreamingDevice(
                    portName,
                    deviceInfo.Name,
                    deviceInfo.SerialNumber,
                    deviceInfo.FirmwareVersion);
                AvailableSerialDevices.Add(serialDevice);
                if (HasNoSerialDevices) { HasNoSerialDevices = false; }
                Common.Loggers.AppLogger.Instance.Information(
                    $"Added DAQiFi device on {portName}: {serialDevice.Name} (S/N: {serialDevice.DeviceSerialNo})");
                return;
            }

            UpdateSerialDeviceMetadata(existing, portName, deviceInfo);
        });
    }

    private SerialStreamingDevice? FindSerialDeviceByPortName(string portName)
    {
        return AvailableSerialDevices.FirstOrDefault(d =>
            d.Port?.PortName.Equals(portName, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static void UpdateSerialDeviceMetadata(
        SerialStreamingDevice serialDevice,
        string portName,
        CoreDeviceInfo deviceInfo)
    {
        serialDevice.Name = !string.IsNullOrWhiteSpace(deviceInfo.Name)
            ? deviceInfo.Name
            : portName;
        serialDevice.DeviceSerialNo = deviceInfo.SerialNumber;
        serialDevice.DeviceVersion = deviceInfo.FirmwareVersion;
    }

    private static void InvokeOnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    #endregion

    #region Desktop Device Event Handlers

    private void HandleWifiDeviceFound(object sender, IDevice device)
    {
        if (device is not DaqifiStreamingDevice wifiDevice)
        {
            return;
        }

        InvokeOnUiThread(() =>
        {
            if (AvailableWiFiDevices.Any(d => d.MacAddress == wifiDevice.MacAddress)) return;
            AvailableWiFiDevices.Add(wifiDevice);
            if (HasNoWiFiDevices) { HasNoWiFiDevices = false; }
        });
    }

    public void Close()
    {
        // Guards against concurrent/double dispose when the window closes while
        // a connect command is still in flight (either path may call this).
        if (_closed) { return; }
        _closed = true;

        StopWiFiDiscovery();
        // Fire-and-forget: cancel discovery and clean up without waiting for task completion
        _ = StopSerialDiscoveryAsync();

        // HID bootloader holds are owned by the app-global watcher and intentionally persist after the
        // dialog closes (so a sitting bootloader stays wedge-proof). Only unsubscribe this dialog from
        // the watcher's list.
        if (_watcher != null)
        {
            ((System.Collections.Specialized.INotifyCollectionChanged)_watcher.Bootloaders).CollectionChanged
                -= OnHidDevicesChanged;
        }
    }

    private void StopWiFiDiscovery()
    {
        _wifiDiscoveryCts?.Cancel();

        if (_wifiFinder != null)
        {
            _wifiFinder.DeviceDiscovered -= HandleCoreWifiDeviceDiscovered;
            _wifiFinder.Dispose();
            _wifiFinder = null;
        }

        _wifiDiscoveryCts?.Dispose();
        _wifiDiscoveryCts = null;
    }

    private async Task StopSerialDiscoveryAsync()
    {
        _serialDiscoveryCts?.Cancel();

        // Wait for the discovery task to complete so that the serial port is fully released
        if (_serialDiscoveryTask != null)
        {
            try
            {
                await _serialDiscoveryTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                Common.Loggers.AppLogger.Instance.Warning("Serial discovery task did not complete within timeout");
            }
            catch (OperationCanceledException)
            {
                // Expected on cancellation
            }
            catch (ObjectDisposedException)
            {
                // Expected during shutdown
            }
            catch (Exception ex)
            {
                Common.Loggers.AppLogger.Instance.Error(ex, "Unexpected error while stopping serial discovery");
            }
            _serialDiscoveryTask = null;
        }

        if (_serialFinder != null)
        {
            _serialFinder.DeviceDiscovered -= HandleCoreSerialDeviceDiscovered;
            _serialFinder.Dispose();
            _serialFinder = null;
        }

        _serialDiscoveryCts?.Dispose();
        _serialDiscoveryCts = null;

    }

    /// <summary>
    /// Stops WiFi discovery and waits for the in-flight discovery cycle to drain before returning —
    /// the async counterpart to <see cref="StopWiFiDiscovery"/>, used before a bootloader flash so a
    /// running UDP discovery cycle isn't still live when the flash begins. Mirrors
    /// <see cref="StopSerialDiscoveryAsync"/>.
    /// </summary>
    private async Task StopWiFiDiscoveryAsync()
    {
        _wifiDiscoveryCts?.Cancel();

        if (_wifiDiscoveryTask != null)
        {
            try
            {
                await _wifiDiscoveryTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                Common.Loggers.AppLogger.Instance.Warning("WiFi discovery task did not complete within timeout");
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                Common.Loggers.AppLogger.Instance.Error(ex, "Unexpected error while stopping WiFi discovery");
            }
            _wifiDiscoveryTask = null;
        }

        if (_wifiFinder != null)
        {
            _wifiFinder.DeviceDiscovered -= HandleCoreWifiDeviceDiscovered;
            _wifiFinder.Dispose();
            _wifiFinder = null;
        }

        _wifiDiscoveryCts?.Dispose();
        _wifiDiscoveryCts = null;
    }

    /// <summary>
    /// Handles duplicate device detection by showing a dialog to the user
    /// </summary>
    private DuplicateDeviceAction HandleDuplicateDevice(DuplicateDeviceCheckResult duplicateResult)
    {
        try
        {
            // Create dialog manually since we need access to the Result property
            var duplicateDialogViewModel = new DuplicateDeviceDialogViewModel(
                duplicateResult.ExistingDevice,
                duplicateResult.NewDevice,
                duplicateResult.ExistingDeviceInterface,
                duplicateResult.NewDeviceInterface);

            var dialog = new DuplicateDeviceDialog();
            dialog.DataContext = duplicateDialogViewModel;
            
            // Find the owner window
            var ownerWindow = System.Windows.Application.Current.Windows
                .Cast<System.Windows.Window>()
                .FirstOrDefault(w => w.DataContext == this);
                
            if (ownerWindow != null)
            {
                dialog.Owner = ownerWindow;
            }
            
            dialog.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;
            
            var dialogResult = dialog.ShowDialog();
            
            if (dialogResult == true)
            {
                return dialog.Result switch
                {
                    DuplicateDeviceDialog.DuplicateDeviceDialogResult.KeepExisting => DuplicateDeviceAction.KeepExisting,
                    DuplicateDeviceDialog.DuplicateDeviceDialogResult.SwitchToNew => DuplicateDeviceAction.SwitchToNew,
                    _ => DuplicateDeviceAction.Cancel
                };
            }

            return DuplicateDeviceAction.Cancel;
        }
        catch (Exception ex)
        {
            // Log error and default to cancel - this should rarely happen
            Daqifi.Desktop.Common.Loggers.AppLogger.Instance.Error(ex, "Failed to show duplicate device dialog");
            return DuplicateDeviceAction.Cancel;
        }
    }
    #endregion
}
