using Daqifi.Desktop.DataModel.Device;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Device.HidDevice;
using Daqifi.Desktop.Device.SerialDevice;
using Daqifi.Desktop.Device.WiFiDevice;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.View;
using System.Collections;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daqifi.Core.Device.Discovery;
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
    private readonly HashSet<string> _probedSerialPorts = new();

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
    public ObservableCollection<HidFirmwareDevice> AvailableHidDevices { get; } = [];

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
        StopSerialDiscovery();

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

        var deviceInfo = new Daqifi.Desktop.DataModel.Device.DeviceInfo
        {
            IpAddress = ManualIpAddress,
            DeviceName = "Manual IP Device",
            Port = 9760 // Common DAQiFi TCP data port - TODO: make configurable or discover dynamically
        };

        var device = new DaqifiStreamingDevice(deviceInfo);
        await ConnectionManager.Instance.Connect(device);
    }

    [RelayCommand]
    private void ConnectHid(object selectedItems)
    {
        //_hidDeviceFinder.Stop();

        var selectedDevices = ((IEnumerable)selectedItems).Cast<HidFirmwareDevice>();
        var hidDevice = selectedDevices.FirstOrDefault();
        if (hidDevice == null) { return; }

        var firmwareDialogViewModel = new FirmwareDialogViewModel(hidDevice);
        _dialogService.ShowDialog<FirmwareDialog>(this, firmwareDialogViewModel);

    }
    #endregion

    #region Core Device Discovery Event Handlers

    private void HandleCoreWifiDeviceDiscovered(object? sender, DeviceDiscoveredEventArgs e)
    {
        try
        {
            var wifiDevice = DeviceInfoConverter.ToWiFiDevice(e.DeviceInfo);
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
            var serialDevice = DeviceInfoConverter.ToSerialDevice(e.DeviceInfo);
            var portName = serialDevice.Port.PortName;

            // Immediately add device to UI with port name
            HandleSerialDeviceFound(sender, serialDevice);

            // Only probe once per port to avoid conflicts
            lock (_probedSerialPorts)
            {
                if (_probedSerialPorts.Contains(portName))
                {
                    return; // Already probed or probing this port
                }
                _probedSerialPorts.Add(portName);
            }

            // Probe device for actual info in background (like old desktop finder did)
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500); // Brief delay to let UI settle
                    if (serialDevice.TryGetDeviceInfo())
                    {
                        // Trigger UI refresh by removing and re-adding with updated info
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            // Find existing device
                            var existing = AvailableSerialDevices.FirstOrDefault(d => d.Port.PortName == portName);
                            if (existing != null)
                            {
                                // Remove and re-add to force UI refresh with updated properties
                                var index = AvailableSerialDevices.IndexOf(existing);
                                AvailableSerialDevices.RemoveAt(index);
                                AvailableSerialDevices.Insert(index, serialDevice);
                            }
                        });
                    }
                }
                catch (Exception probEx)
                {
                    // Log but don't fail - device still shows with port name only
                    Common.Loggers.AppLogger.Instance.Warning($"Failed to retrieve device info for {portName}: {probEx.Message}");

                    // Remove from probed set so we can retry later
                    lock (_probedSerialPorts)
                    {
                        _probedSerialPorts.Remove(portName);
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Common.Loggers.AppLogger.Instance.Error(ex, "Error handling Serial device discovery");
        }
    }

    private void HandleCoreHidDeviceDiscovered(object? sender, DeviceDiscoveredEventArgs e)
    {
        try
        {
            // HID devices are firmware devices for bootloader mode
            // For now, core HID finder returns empty, so this won't be called often
            // TODO: Create HID device from core IDeviceInfo when HID library is added
            Common.Loggers.AppLogger.Instance.Information($"HID device discovered: {e.DeviceInfo.Name}");
        }
        catch (Exception ex)
        {
            Common.Loggers.AppLogger.Instance.Error(ex, "Error handling HID device discovery");
        }
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
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
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
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
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

    private void HandleHidDeviceFound(object sender, IDevice device)
    {
        if (device is not HidFirmwareDevice hidDevice)
        {
            return;
        }

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            AvailableHidDevices.Add(hidDevice);
            if (HasNoHidDevices) { HasNoHidDevices = false; }
        });
    }

    private void HandleHidDeviceRemoved(object sender, IDevice device)
    {
        if (device is not HidFirmwareDevice hidDevice)
        {
            return;
        }

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            AvailableHidDevices.Remove(hidDevice);
            if (AvailableHidDevices.Count == 0) { HasNoHidDevices = true; }
        });
    }

    public void Close()
    {
        StopWiFiDiscovery();
        StopSerialDiscovery();
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

    private void StopSerialDiscovery()
    {
        _serialDiscoveryCts?.Cancel();

        if (_serialFinder != null)
        {
            _serialFinder.DeviceDiscovered -= HandleCoreSerialDeviceDiscovered;
            _serialFinder.Dispose();
            _serialFinder = null;
        }

        _serialDiscoveryCts?.Dispose();
        _serialDiscoveryCts = null;

        // Clear probed ports so they can be probed again next time
        lock (_probedSerialPorts)
        {
            _probedSerialPorts.Clear();
        }
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