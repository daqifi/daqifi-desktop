using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Helpers;
using Daqifi.Desktop.Logger;
using Brush = System.Windows.Media.Brush;
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

    /// <summary>
    /// The device that owns <see cref="SelectedChannel"/>, while the settings drawer is
    /// open. Exposes device-level settings the drawer edits alongside the channel — the
    /// device-wide PWM frequency (issue #664).
    /// </summary>
    [ObservableProperty] private Daqifi.Desktop.Device.IStreamingDevice? _selectedDevice;

    [ObservableProperty] private bool _isSettingsOpen;

    /// <summary>
    /// Mirrors <see cref="LoggingManager.Active"/>. While true, channel
    /// activation, select-all, and clear-all are disabled — adding or
    /// removing subscribed channels mid-session would corrupt the
    /// per-session device metadata captured at session start.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleChannelCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearAllCommand))]
    private bool _isLoggingActive;

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
            result[i] = TileBrushes.Frozen(hexes[i]);
        }
        return result;
    }

    /// <summary>Creates the view-model and begins watching for connected devices.</summary>
    public ChannelsPaneViewModel()
    {
        _valueRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _valueRefreshTimer.Tick += OnValueRefreshTick;
        _valueRefreshTimer.Start();

        ConnectionManager.Instance.PropertyChanged += OnConnectionManagerPropertyChanged;
        LoggingManager.Instance.PropertyChanged += OnLoggingManagerPropertyChanged;
        IsLoggingActive = LoggingManager.Instance.Active;
        Rebuild();
    }

    /// <summary>
    /// Gates channel activation, select-all, and clear-all: disabled while a
    /// logging session is active. Re-queried when <see cref="IsLoggingActive"/> changes.
    /// </summary>
    private bool CanModifyChannels() => !IsLoggingActive;

    private void OnLoggingManagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoggingManager.Active))
        {
            // Marshal to the UI thread — Active can be toggled from background
            // threads (e.g., disk-space monitor stopping a session).
            var dispatcher = _valueRefreshTimer.Dispatcher;
            if (dispatcher.CheckAccess())
            {
                IsLoggingActive = LoggingManager.Instance.Active;
            }
            else
            {
                dispatcher.BeginInvoke(() => IsLoggingActive = LoggingManager.Instance.Active);
            }
        }
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
                else if (channel.IsDigital &&
                         (channel.Direction == ChannelDirection.Output || channel.IsPwmEnabled))
                {
                    // A PWM-active channel drives its pin regardless of the stored
                    // direction, so it shelves with the outputs (issue #664).
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

    /// <summary>Toggles the clicked channel between active and inactive.</summary>
    [RelayCommand(CanExecute = nameof(CanModifyChannels))]
    private void ToggleChannel(ChannelTileViewModel? tile)
    {
        if (tile == null) return;
        var channel = tile.Channel;
        var device = FindOwningDevice(channel);
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

    /// <summary>Activates every channel in a section ("AI", "DI", or "DO").</summary>
    [RelayCommand(CanExecute = nameof(CanModifyChannels))]
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

    /// <summary>Deactivates every channel across all sections.</summary>
    [RelayCommand(CanExecute = nameof(CanModifyChannels))]
    private void ClearAll()
    {
        var active = AnalogInputs
            .Concat(DigitalInputs)
            .Concat(DigitalOutputs)
            .Where(t => t.IsActive)
            .ToList();
        foreach (var tile in active) ToggleChannel(tile);
    }

    /// <summary>Opens the inline settings drawer for a channel.</summary>
    [RelayCommand]
    private void OpenSettings(ChannelTileViewModel? tile)
    {
        if (tile == null) return;
        SelectedChannel = tile.Channel;
        SelectedDevice = FindOwningDevice(tile.Channel);
        IsSettingsOpen = true;
    }

    /// <summary>
    /// Resolves the connected device that owns <paramref name="channel"/>. Enumerates a
    /// snapshot of the connected-device list (like <see cref="Rebuild"/>) because the
    /// list mutates during connect/disconnect flows.
    /// </summary>
    private static Daqifi.Desktop.Device.IStreamingDevice? FindOwningDevice(IChannel channel)
    {
        return ConnectionManager.Instance.ConnectedDevices.ToList()
            .FirstOrDefault(d => d.DeviceSerialNo == channel.DeviceSerialNo);
    }

    /// <summary>Closes the inline settings drawer.</summary>
    [RelayCommand]
    private void CloseSettings()
    {
        IsSettingsOpen = false;
        SelectedChannel = null;
        SelectedDevice = null;
    }

    /// <summary>Sets the selected channel's plot color.</summary>
    [RelayCommand]
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
        LoggingManager.Instance.PropertyChanged -= OnLoggingManagerPropertyChanged;

        DisposeTiles(AnalogInputs);
        DisposeTiles(DigitalInputs);
        DisposeTiles(DigitalOutputs);

        GC.SuppressFinalize(this);
    }
}
