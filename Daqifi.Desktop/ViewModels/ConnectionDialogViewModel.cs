using Daqifi.Desktop.Device;
using Daqifi.Desktop.Device.SerialDevice;
using Daqifi.Desktop.Device.WiFiDevice;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.View;
using System.Collections;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private Daqifi.Core.Device.Discovery.HidDeviceFinder? _hidDeviceFinder;
    private CancellationTokenSource? _wifiDiscoveryCts;
    private CancellationTokenSource? _serialDiscoveryCts;
    private CancellationTokenSource? _hidDiscoveryCts;
    private Task? _wifiDiscoveryTask;
    private Task? _serialDiscoveryTask;
    private Task? _hidDiscoveryTask;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private bool _hasNoWiFiDevices = true;

    [ObservableProperty]
    private bool _hasNoSerialDevices = true;

    [ObservableProperty]
    private bool _hasNoHidDevices = true;
    #endregion

    #region Properties
    public ObservableCollection<DaqifiStreamingDevice> AvailableWiFiDevices { get; } = [];
    public ObservableCollection<SerialStreamingDevice> AvailableSerialDevices { get; } = [];
    public ObservableCollection<CoreDeviceInfo> AvailableHidDevices { get; } = [];

    public string ManualPortName { get; set; }

    public SerialStreamingDevice ManualSerialDevice { get; set; }

    public string ManualIpAddress { get; set; }
    #endregion

    #region Constructor
    public ConnectionDialogViewModel() : this(ServiceLocator.Resolve<IDialogService>()) { }

    public ConnectionDialogViewModel(IDialogService dialogService)
    {
        _dialogService = dialogService;
        ConnectCommand = new AsyncRelayCommand<object>(ConnectAsync);
        ConnectSerialCommand = new AsyncRelayCommand<object>(ConnectSerialAsync);
        ConnectManualSerialCommand = new AsyncRelayCommand(ConnectManualSerialAsync);
        ConnectManualWifiCommand = new AsyncRelayCommand(ConnectManualWifiAsync);
        
        // Set up the duplicate device handler
        ConnectionManager.Instance.DuplicateDeviceHandler = HandleDuplicateDevice;
    }

    public void StartConnectionFinders()
    {
        // WiFi Discovery
        _wifiFinder = new WiFiDeviceFinder(30303);
        _wifiDiscoveryCts = new CancellationTokenSource();
        _wifiFinder.DeviceDiscovered += HandleCoreWifiDeviceDiscovered;
        _wifiDiscoveryTask = RunContinuousWiFiDiscoveryAsync(_wifiDiscoveryCts.Token);

        // Serial Discovery
        _serialFinder = new Daqifi.Core.Device.Discovery.SerialDeviceFinder();
        _serialDiscoveryCts = new CancellationTokenSource();
        _serialFinder.DeviceDiscovered += HandleCoreSerialDeviceDiscovered;
        _serialDiscoveryTask = RunContinuousSerialDiscoveryAsync(_serialDiscoveryCts.Token);

        // HID Discovery
        _hidDeviceFinder = new Daqifi.Core.Device.Discovery.HidDeviceFinder();
        _hidDiscoveryCts = new CancellationTokenSource();
        _hidDeviceFinder.DeviceDiscovered += HandleCoreHidDeviceDiscovered;
        _hidDiscoveryTask = RunContinuousHidDiscoveryAsync(_hidDiscoveryCts.Token);
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

    private async Task RunContinuousHidDiscoveryAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _hidDeviceFinder != null)
            {
                await _hidDeviceFinder.DiscoverAsync(cancellationToken);
                // HID discovery is quick, pause longer between scans
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
            Common.Loggers.AppLogger.Instance.Error(ex, "Error in HID discovery loop");
        }
    }
    #endregion

    #region Commands

    public IAsyncRelayCommand ConnectCommand { get; }
    public IAsyncRelayCommand ConnectSerialCommand { get; }
    public IAsyncRelayCommand ConnectManualSerialCommand { get; }
    public IAsyncRelayCommand ConnectManualWifiCommand { get; }

    private async Task ConnectAsync(object selectedItems)
    {
        StopWiFiDiscovery();

        var selectedDevices = ((IEnumerable)selectedItems).Cast<IStreamingDevice>();

        foreach (var device in selectedDevices)
        {
            await ConnectionManager.Instance.Connect(device);
        }
    }

    private async Task ConnectSerialAsync(object selectedItems)
    {
        await StopSerialDiscoveryAsync();

        var selectedDevices = ((IEnumerable)selectedItems).Cast<IStreamingDevice>();
        foreach (var device in selectedDevices)
        {
            await ConnectionManager.Instance.Connect(device);
        }
    }

    private async Task ConnectManualSerialAsync()
    {
        if (string.IsNullOrWhiteSpace(ManualPortName)) { return; }

        ManualSerialDevice = new SerialStreamingDevice(ManualPortName);
        await ConnectionManager.Instance.Connect(ManualSerialDevice);
    }

    private async Task ConnectManualWifiAsync()
    {
        if (string.IsNullOrWhiteSpace(ManualIpAddress)) { return; }

        var endpointInput = ManualIpAddress.Trim();
        IPAddress? ipAddress;
        try
        {
            ipAddress = await ResolveManualWifiEndpointAsync(endpointInput);
        }
        catch (ArgumentException ex)
        {
            Common.Loggers.AppLogger.Instance.Warning(
                $"Manual WiFi connection requires a valid IP address or host name. " +
                $"Received '{ManualIpAddress}': {ex.Message}");
            return;
        }
        catch (SocketException ex)
        {
            Common.Loggers.AppLogger.Instance.Warning(
                $"Failed to resolve manual WiFi endpoint '{ManualIpAddress}': {ex.Message}");
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
    private void ConnectHid(object selectedItems)
    {
        //_hidDeviceFinder.Stop();

        var selectedDevices = ((IEnumerable)selectedItems).Cast<CoreDeviceInfo>();
        var hidDevice = selectedDevices.FirstOrDefault();
        if (hidDevice == null) { return; }

        var firmwareDialogViewModel = new FirmwareDialogViewModel(hidDevice.Name);
        _dialogService.ShowDialog<FirmwareDialog>(this, firmwareDialogViewModel);

    }
    #endregion

    #region Core Device Discovery Event Handlers

    private void HandleCoreWifiDeviceDiscovered(object? sender, DeviceDiscoveredEventArgs e)
    {
        try
        {
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

    private void HandleCoreHidDeviceDiscovered(object? sender, DeviceDiscoveredEventArgs e)
    {
        try
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var discoveredDevice = e.DeviceInfo;
                var discoveredKey = BuildHidDeviceKey(discoveredDevice);
                var alreadyTracked = AvailableHidDevices.Any(device => BuildHidDeviceKey(device) == discoveredKey);
                if (alreadyTracked)
                {
                    return;
                }

                AvailableHidDevices.Add(discoveredDevice);
                HasNoHidDevices = AvailableHidDevices.Count == 0;
            });
        }
        catch (Exception ex)
        {
            Common.Loggers.AppLogger.Instance.Error(ex, "Error handling HID device discovery");
        }
    }

    private static string BuildHidDeviceKey(CoreDeviceInfo deviceInfo)
    {
        return $"{deviceInfo.DevicePath}|{deviceInfo.SerialNumber}|{deviceInfo.Name}";
    }

    #endregion

    #region Desktop Device Event Handlers

    private void HandleWifiDeviceFound(object sender, IDevice device)
    {
        if (device is not DaqifiStreamingDevice wifiDevice)
        {
            return;
        }

        if (AvailableWiFiDevices.FirstOrDefault(d => d.MacAddress == wifiDevice.MacAddress) == null)
        {
            InvokeOnUiThread(() =>
            {
                AvailableWiFiDevices.Add(wifiDevice);
                if (HasNoWiFiDevices) { HasNoWiFiDevices = false; }
            });
        }
    }

    private void HandleWifiDeviceRemoved(object sender, IDevice device)
    {
        if (device is not DaqifiStreamingDevice wifiDevice)
        {
            return;
        }

        var matchingDevice = AvailableWiFiDevices.FirstOrDefault(d => d.MacAddress == wifiDevice.MacAddress);
        if (matchingDevice != null)
        {
            InvokeOnUiThread(() =>
            {
                AvailableWiFiDevices.Remove(matchingDevice);
            });
        }
    }

    private void HandleSerialDeviceFound(object sender, IDevice device)
    {
        if (device is not SerialStreamingDevice serialDevice)
        {
            return;
        }

        if (AvailableSerialDevices.FirstOrDefault(d => d.Port.PortName == serialDevice.Port.PortName) == null)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                AvailableSerialDevices.Add(serialDevice);
                if (HasNoSerialDevices) { HasNoSerialDevices = false; }
            });
        }
    }

    private void HandleSerialDeviceRemoved(object sender, IDevice device)
    {
        if (device is not SerialStreamingDevice serialDevice)
        {
            return;
        }

        var matchingDevice = AvailableSerialDevices.FirstOrDefault(d => d.Port.PortName == serialDevice.Port.PortName);
        if (matchingDevice != null)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                AvailableSerialDevices.Remove(matchingDevice);
            });
        }
    }

    public void Close()
    {
        StopWiFiDiscovery();
        // Fire-and-forget: cancel discovery and clean up without waiting for task completion
        _ = StopSerialDiscoveryAsync();
        StopHidDiscovery();
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

    private void StopHidDiscovery()
    {
        _hidDiscoveryCts?.Cancel();

        if (_hidDeviceFinder != null)
        {
            _hidDeviceFinder.DeviceDiscovered -= HandleCoreHidDeviceDiscovered;
            _hidDeviceFinder.Dispose();
            _hidDeviceFinder = null;
        }

        _hidDiscoveryCts?.Dispose();
        _hidDiscoveryCts = null;
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
