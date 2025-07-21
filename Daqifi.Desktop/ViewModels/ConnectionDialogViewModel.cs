using Daqifi.Desktop.DataModel.Device;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Device.HidDevice;
using Daqifi.Desktop.Device.SerialDevice;
using Daqifi.Desktop.Device.WiFiDevice;
using Daqifi.Desktop.DialogService;
using DAQifi.Desktop.View;
using DAQifi.Desktop.ViewModels;
using System.Collections;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Daqifi.Desktop.ViewModels;

public partial class ConnectionDialogViewModel : ObservableObject
{
    #region Private Variables
    private IDeviceFinder _wifiFinder;
    private SerialDeviceFinder _serialFinder;
    private HidDeviceFinder _hidDeviceFinder;
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
    }

    public void StartConnectionFinders()
    {
        // Use the backward-compatible finder that handles firmware responding to port 30303
        _wifiFinder = new DaqifiDeviceFinderBackwardCompatible(30303);
        _wifiFinder.OnDeviceFound += HandleWifiDeviceFound;
        _wifiFinder.OnDeviceRemoved += HandleWifiDeviceRemoved;
        _wifiFinder.Start();

        _serialFinder = new SerialDeviceFinder();
        _serialFinder.OnDeviceFound += HandleSerialDeviceFound;
        _serialFinder.OnDeviceRemoved += HandleSerialDeviceRemoved;
        _serialFinder.Start();

        _hidDeviceFinder = new HidDeviceFinder();
        _hidDeviceFinder.OnDeviceFound += HandleHidDeviceFound;
        _hidDeviceFinder.OnDeviceRemoved += HandleHidDeviceRemoved;
        _hidDeviceFinder.Start();
    }
    #endregion

    #region Commands

    public IAsyncRelayCommand ConnectCommand { get; }
    public IAsyncRelayCommand ConnectSerialCommand { get; }
    public IAsyncRelayCommand ConnectManualSerialCommand { get; }
    public IAsyncRelayCommand ConnectManualWifiCommand { get; }

    private async Task ConnectAsync(object selectedItems)
    {
        _wifiFinder.Stop();

        var selectedDevices = ((IEnumerable)selectedItems).Cast<IStreamingDevice>();
        
        foreach (var device in selectedDevices)
        {
            await ConnectionManager.Instance.Connect(device);
        }
    }

    private async Task ConnectSerialAsync(object selectedItems)
    {
        _serialFinder.Stop();

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

        var deviceInfo = new DeviceInfo
        {
            IpAddress = ManualIpAddress,
            DeviceName = "Manual IP Device"
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

        if (AvailableSerialDevices.FirstOrDefault(d => d.Port == serialDevice.Port) == null)
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
        _wifiFinder?.Stop();
        _serialFinder?.Stop();
        _hidDeviceFinder?.Stop();
    }
}