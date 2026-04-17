using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Helpers;
using Daqifi.Desktop.Logger;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using ChannelDirection = Daqifi.Core.Channel.ChannelDirection;

namespace Daqifi.Desktop.ViewModels;

/// <summary>
/// Backs the unified Channels pane. Aggregates channels across every
/// connected device into AI/DI/DO sections, drives the 10 Hz live-value
/// refresh, and owns the inline settings drawer state.
/// </summary>
public partial class ChannelsPaneViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _valueRefreshTimer;
    private bool _disposed;

    /// <summary>
    /// Fires at the refresh cadence so tiles can re-read their live value
    /// without each tile owning its own timer.
    /// </summary>
    public event EventHandler? ValueRefresh;

    /// <summary>Analog input tiles aggregated across every connected device.</summary>
    public ObservableCollection<ChannelTileViewModel> AnalogInputs { get; } = [];

    /// <summary>Digital input tiles aggregated across every connected device.</summary>
    public ObservableCollection<ChannelTileViewModel> DigitalInputs { get; } = [];

    /// <summary>Digital output tiles aggregated across every connected device.</summary>
    public ObservableCollection<ChannelTileViewModel> DigitalOutputs { get; } = [];

    /// <summary>
    /// Display names of every connected device, shown as chips in the header
    /// when more than one device is connected. Populated in the order
    /// returned by <see cref="ConnectionManager.ConnectedDevices"/>.
    /// </summary>
    public ObservableCollection<string> ConnectedDeviceNames { get; } = [];

    [ObservableProperty] private bool _hasConnectedDevice;
    [ObservableProperty] private bool _hasMultipleDevices;
    [ObservableProperty] private string _deviceName = "";
    [ObservableProperty] private int _activeAnalogCount;
    [ObservableProperty] private int _totalAnalogCount;
    [ObservableProperty] private int _activeDigitalInCount;
    [ObservableProperty] private int _totalDigitalInCount;
    [ObservableProperty] private int _activeDigitalOutCount;
    [ObservableProperty] private int _totalDigitalOutCount;
    [ObservableProperty] private int _totalActive;

    [ObservableProperty] private IChannel? _selectedChannel;
    [ObservableProperty] private bool _isSettingsOpen;

    private static readonly Brush[] ColorPaletteBrushes = BuildPalette(
    [
        "#4A9EFF", "#4ADE80", "#F59E0B", "#F43F5E",
        "#A855F7", "#06B6D4", "#EC4899", "#FACC15",
    ]);

    /// <summary>
    /// The palette shown in the settings drawer. Exposed as an instance
    /// property so WPF's Binding engine can resolve it via the DataContext.
    /// </summary>
    public Brush[] Palette => ColorPaletteBrushes;

    private static Brush[] BuildPalette(string[] hexes)
    {
        var result = new Brush[hexes.Length];
        for (var i = 0; i < hexes.Length; i++)
        {
            var color = (Color)ColorConverter.ConvertFromString(hexes[i])!;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            result[i] = brush;
        }
        return result;
    }

    /// <summary>Toggles the clicked channel between active and inactive.</summary>
    public IRelayCommand<ChannelTileViewModel> ToggleChannelCommand { get; }

    /// <summary>Activates every channel in a section ("AI", "DI", or "DO").</summary>
    public IRelayCommand<string> SelectAllCommand { get; }

    /// <summary>Deactivates every channel across all sections.</summary>
    public IRelayCommand ClearAllCommand { get; }

    /// <summary>Opens the inline settings drawer for a channel.</summary>
    public IRelayCommand<ChannelTileViewModel> OpenSettingsCommand { get; }

    /// <summary>Closes the inline settings drawer.</summary>
    public IRelayCommand CloseSettingsCommand { get; }

    /// <summary>Sets the selected channel's plot color.</summary>
    public IRelayCommand<Brush> SetColorCommand { get; }

    /// <summary>Creates the view-model and begins watching for connected devices.</summary>
    public ChannelsPaneViewModel()
    {
        ToggleChannelCommand = new RelayCommand<ChannelTileViewModel>(ToggleChannel);
        SelectAllCommand = new RelayCommand<string>(SelectAll);
        ClearAllCommand = new RelayCommand(ClearAll);
        OpenSettingsCommand = new RelayCommand<ChannelTileViewModel>(OpenSettings);
        CloseSettingsCommand = new RelayCommand(CloseSettings);
        SetColorCommand = new RelayCommand<Brush>(SetColor);

        _valueRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _valueRefreshTimer.Tick += OnValueRefreshTick;
        _valueRefreshTimer.Start();

        ConnectionManager.Instance.PropertyChanged += OnConnectionManagerPropertyChanged;
        Rebuild();
    }

    private void OnValueRefreshTick(object? sender, EventArgs e) =>
        ValueRefresh?.Invoke(this, EventArgs.Empty);

    private void OnConnectionManagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectionManager.ConnectedDevices))
        {
            Rebuild();
        }
    }

    /// <summary>
    /// Called by a tile when its channel's direction/output flips, so the
    /// tile can be re-shelved into the right section and its stripe/label
    /// updated. Deferred to the dispatcher to avoid mutating collections
    /// during the channel's own PropertyChanged notification.
    /// </summary>
    internal void RequestSectionReshuffle()
    {
        _valueRefreshTimer.Dispatcher.BeginInvoke((Action)Rebuild, DispatcherPriority.Background);
    }

    private void Rebuild()
    {
        if (_disposed) return;

        DisposeTiles(AnalogInputs);
        DisposeTiles(DigitalInputs);
        DisposeTiles(DigitalOutputs);
        ConnectedDeviceNames.Clear();

        var devices = ConnectionManager.Instance.ConnectedDevices.ToList();
        HasConnectedDevice = devices.Count > 0;
        HasMultipleDevices = devices.Count > 1;
        DeviceName = devices.FirstOrDefault()?.Name ?? "";

        foreach (var device in devices)
        {
            ConnectedDeviceNames.Add(device.Name);
        }

        if (devices.Count == 0)
        {
            RecomputeCounts();
            return;
        }

        foreach (var device in devices)
        {
            foreach (var channel in device.DataChannels.NaturalOrderBy(c => c.Name))
            {
                var tile = new ChannelTileViewModel(channel, this, device.Name, HasMultipleDevices);
                if (channel.IsAnalog && channel.Direction == ChannelDirection.Input)
                {
                    AnalogInputs.Add(tile);
                }
                else if (channel.IsDigital && channel.Direction == ChannelDirection.Output)
                {
                    DigitalOutputs.Add(tile);
                }
                else if (channel.IsDigital)
                {
                    DigitalInputs.Add(tile);
                }
                else
                {
                    tile.Dispose();
                }
            }
        }
        RecomputeCounts();
    }

    private static void DisposeTiles(ObservableCollection<ChannelTileViewModel> tiles)
    {
        foreach (var tile in tiles) tile.Dispose();
        tiles.Clear();
    }

    private void RecomputeCounts()
    {
        ActiveAnalogCount = AnalogInputs.Count(t => t.IsActive);
        TotalAnalogCount = AnalogInputs.Count;
        ActiveDigitalInCount = DigitalInputs.Count(t => t.IsActive);
        TotalDigitalInCount = DigitalInputs.Count;
        ActiveDigitalOutCount = DigitalOutputs.Count(t => t.IsActive);
        TotalDigitalOutCount = DigitalOutputs.Count;
        TotalActive = ActiveAnalogCount + ActiveDigitalInCount + ActiveDigitalOutCount;
    }

    private void ToggleChannel(ChannelTileViewModel? tile)
    {
        if (tile == null) return;
        var channel = tile.Channel;
        var device = ConnectionManager.Instance.ConnectedDevices
            .FirstOrDefault(d => d.DeviceSerialNo == channel.DeviceSerialNo);
        if (device == null) return;

        if (channel.IsActive)
        {
            LoggingManager.Instance.Unsubscribe(channel);
            device.RemoveChannel(channel);
        }
        else
        {
            device.AddChannel(channel);
            LoggingManager.Instance.Subscribe(channel);
        }
        RecomputeCounts();
    }

    private void SelectAll(string? section)
    {
        IEnumerable<ChannelTileViewModel> tiles = section switch
        {
            "AI" => AnalogInputs,
            "DI" => DigitalInputs,
            "DO" => DigitalOutputs,
            _ => [],
        };
        foreach (var tile in tiles.Where(t => !t.IsActive).ToList())
        {
            ToggleChannel(tile);
        }
    }

    private void ClearAll()
    {
        var active = AnalogInputs
            .Concat(DigitalInputs)
            .Concat(DigitalOutputs)
            .Where(t => t.IsActive)
            .ToList();
        foreach (var tile in active) ToggleChannel(tile);
    }

    private void OpenSettings(ChannelTileViewModel? tile)
    {
        if (tile == null) return;
        SelectedChannel = tile.Channel;
        IsSettingsOpen = true;
    }

    private void CloseSettings()
    {
        IsSettingsOpen = false;
        SelectedChannel = null;
    }

    private void SetColor(Brush? brush)
    {
        if (SelectedChannel == null || brush == null) return;
        SelectedChannel.ChannelColorBrush = brush;
    }

    /// <summary>
    /// Stops the refresh timer, detaches the singleton subscription, and
    /// disposes every tile. Safe to call more than once.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _valueRefreshTimer.Stop();
        _valueRefreshTimer.Tick -= OnValueRefreshTick;
        ConnectionManager.Instance.PropertyChanged -= OnConnectionManagerPropertyChanged;

        DisposeTiles(AnalogInputs);
        DisposeTiles(DigitalInputs);
        DisposeTiles(DigitalOutputs);

        GC.SuppressFinalize(this);
    }
}
