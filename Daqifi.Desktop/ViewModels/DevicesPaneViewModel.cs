using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daqifi.Desktop.Device;

namespace Daqifi.Desktop.ViewModels;

/// <summary>
/// Backs the unified Devices pane. Aggregates connected devices into a tile
/// grid and owns the inline settings drawer state. Delegates side-effecting
/// commands (connect, disconnect, reboot, firmware, network) to the shell
/// view-model so the existing service wiring is reused unchanged.
/// </summary>
public partial class DevicesPaneViewModel : ObservableObject, IDisposable
{
    private readonly DaqifiViewModel? _shell;
    private readonly Dispatcher _dispatcher;
    private bool _disposed;

    /// <summary>Device tiles in the order ConnectionManager returns them.</summary>
    public ObservableCollection<DeviceTileViewModel> Devices { get; } = [];

    /// <summary>
    /// The shell view-model, exposed so the drawer can bind directly to shell-owned
    /// state (firmware path/progress, logging mode, SD format) and commands
    /// (firmware upload, network apply) without a second DataContext hop.
    /// </summary>
    public DaqifiViewModel? Shell => _shell;

    [ObservableProperty] private bool _hasConnectedDevice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDevice))]
    [NotifyPropertyChangedFor(nameof(SelectedDeviceSupportsFirmwareUpdate))]
    [NotifyPropertyChangedFor(nameof(FrequencyHz))]
    private DeviceTileViewModel? _selectedTile;

    [ObservableProperty] private bool _isSettingsOpen;

    /// <summary>The underlying device of the tile shown in the settings drawer.</summary>
    public IStreamingDevice? SelectedDevice => SelectedTile?.Device;

    /// <summary>True when the selected device is USB-connected (firmware update path).</summary>
    public bool SelectedDeviceSupportsFirmwareUpdate =>
        SelectedDevice?.ConnectionType == ConnectionType.Usb;

    /// <summary>
    /// Sampling frequency shown on the drawer's FREQUENCY slider.
    /// Reads straight off the device so the slider reflects the real value,
    /// but writes go through the shell's guarded <c>SelectedStreamingFrequency</c>
    /// setter — which blocks changes and shows an error dialog while a
    /// logging session is active.
    /// </summary>
    public int FrequencyHz
    {
        get => SelectedDevice?.StreamingFrequency ?? 0;
        set
        {
            if (_shell == null || SelectedDevice == null) return;
            _shell.SelectedStreamingFrequency = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Opens the inline settings drawer for a device tile.</summary>
    public IRelayCommand<DeviceTileViewModel> OpenSettingsCommand { get; }

    /// <summary>Closes the inline settings drawer.</summary>
    public IRelayCommand CloseSettingsCommand { get; }

    /// <summary>Shows the Add-Device dialog via the shell view-model.</summary>
    public IRelayCommand AddDeviceCommand { get; }

    /// <summary>Disconnects the currently selected device and closes the drawer.</summary>
    public IRelayCommand DisconnectSelectedCommand { get; }

    /// <summary>Reboots the currently selected device.</summary>
    public IRelayCommand RebootSelectedCommand { get; }

    /// <summary>
    /// Writes the chosen logging-mode label ("Stream to App" or "Log to
    /// Device") to the shell. Parameterized so the XAML can wire both
    /// segmented-toggle RadioButtons to the same command.
    /// </summary>
    public IRelayCommand<string> SetLoggingModeCommand { get; }

    /// <summary>Creates the view-model bound to the shell view-model.</summary>
    public DevicesPaneViewModel(DaqifiViewModel? shell)
    {
        _shell = shell;
        _dispatcher = Dispatcher.CurrentDispatcher;

        OpenSettingsCommand = new RelayCommand<DeviceTileViewModel>(OpenSettings);
        CloseSettingsCommand = new RelayCommand(CloseSettings);
        AddDeviceCommand = new RelayCommand(AddDevice);
        DisconnectSelectedCommand = new RelayCommand(DisconnectSelected, () => SelectedDevice != null);
        RebootSelectedCommand = new RelayCommand(RebootSelected, () => SelectedDevice != null);
        SetLoggingModeCommand = new RelayCommand<string>(SetLoggingMode);

        ConnectionManager.Instance.PropertyChanged += OnConnectionManagerPropertyChanged;
        Rebuild();
    }

    partial void OnSelectedTileChanged(DeviceTileViewModel? value)
    {
        // Derived-property change notifications are declared via
        // [NotifyPropertyChangedFor] attributes above; the partial is only
        // for imperative work the source generator can't do.
        DisconnectSelectedCommand.NotifyCanExecuteChanged();
        RebootSelectedCommand.NotifyCanExecuteChanged();
    }

    private void OnConnectionManagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ConnectionManager.ConnectedDevices)) return;

        if (_dispatcher.CheckAccess())
        {
            Rebuild();
        }
        else
        {
            _dispatcher.BeginInvoke((Action)Rebuild);
        }
    }

    private void Rebuild()
    {
        if (_disposed) return;

        var openSerial = SelectedDevice?.DeviceSerialNo;

        foreach (var tile in Devices) tile.Dispose();
        Devices.Clear();

        foreach (var device in ConnectionManager.Instance.ConnectedDevices)
        {
            Devices.Add(new DeviceTileViewModel(device));
        }

        HasConnectedDevice = Devices.Count > 0;

        // Preserve drawer state across a rebuild if the device is still there;
        // otherwise close the drawer so it doesn't point at a removed device.
        if (IsSettingsOpen && openSerial != null)
        {
            var match = Devices.FirstOrDefault(d => d.Device.DeviceSerialNo == openSerial);
            if (match != null)
            {
                SelectedTile = match;
            }
            else
            {
                CloseSettings();
            }
        }
    }

    private void OpenSettings(DeviceTileViewModel? tile)
    {
        if (tile == null) return;
        SelectedTile = tile;
        // Keep the shell's SelectedDevice in sync so existing commands
        // (UploadFirmware, UpdateNetworkConfiguration) operate on the
        // device the drawer is showing.
        if (_shell != null)
        {
            _shell.SelectedDevice = tile.Device;
            _shell.SelectedDeviceSupportsFirmwareUpdate =
                tile.Device.ConnectionType == ConnectionType.Usb;
        }
        IsSettingsOpen = true;
    }

    private void CloseSettings()
    {
        IsSettingsOpen = false;
        SelectedTile = null;
    }

    private void AddDevice()
    {
        if (_shell?.ShowConnectionDialogCommand.CanExecute(null) == true)
        {
            _shell.ShowConnectionDialogCommand.Execute(null);
        }
    }

    private void DisconnectSelected()
    {
        var device = SelectedDevice;
        if (device == null || _shell == null) return;
        if (_shell.DisconnectDeviceCommand.CanExecute(device))
        {
            _shell.DisconnectDeviceCommand.Execute(device);
        }
        CloseSettings();
    }

    private void SetLoggingMode(string? mode)
    {
        // The shell setter takes the label, drives device SwitchMode, and
        // refuses changes mid-session — we just forward the click.
        if (string.IsNullOrEmpty(mode) || _shell == null) return;
        _shell.SelectedLoggingMode = mode;
    }

    private void RebootSelected()
    {
        var device = SelectedDevice;
        if (device == null || _shell == null) return;
        if (_shell.RebootDeviceCommand.CanExecute(device))
        {
            _shell.RebootDeviceCommand.Execute(device);
        }
    }

    /// <summary>Detaches the singleton subscription and disposes tiles.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ConnectionManager.Instance.PropertyChanged -= OnConnectionManagerPropertyChanged;

        foreach (var tile in Devices) tile.Dispose();
        Devices.Clear();

        GC.SuppressFinalize(this);
    }
}
