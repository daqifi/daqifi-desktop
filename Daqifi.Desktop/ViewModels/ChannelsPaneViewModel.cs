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

public partial class ChannelsPaneViewModel : ObservableObject
{
    private readonly DispatcherTimer _valueRefreshTimer;
    public event EventHandler? ValueRefresh;

    public ObservableCollection<ChannelTileViewModel> AnalogInputs { get; } = [];
    public ObservableCollection<ChannelTileViewModel> DigitalInputs { get; } = [];
    public ObservableCollection<ChannelTileViewModel> DigitalOutputs { get; } = [];

    [ObservableProperty] private bool _hasConnectedDevice;
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

    public static Brush[] ColorPalette { get; } = BuildPalette(
    [
        "#4A9EFF", "#4ADE80", "#F59E0B", "#F43F5E",
        "#A855F7", "#06B6D4", "#EC4899", "#FACC15",
    ]);

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

    public IRelayCommand<ChannelTileViewModel> ToggleChannelCommand { get; }
    public IRelayCommand<string> SelectAllCommand { get; }
    public IRelayCommand ClearAllCommand { get; }
    public IRelayCommand<ChannelTileViewModel> OpenSettingsCommand { get; }
    public IRelayCommand CloseSettingsCommand { get; }
    public IRelayCommand<Brush> SetColorCommand { get; }

    public ChannelsPaneViewModel()
    {
        ToggleChannelCommand = new RelayCommand<ChannelTileViewModel>(ToggleChannel);
        SelectAllCommand = new RelayCommand<string>(SelectAll);
        ClearAllCommand = new RelayCommand(ClearAll);
        OpenSettingsCommand = new RelayCommand<ChannelTileViewModel>(OpenSettings);
        CloseSettingsCommand = new RelayCommand(CloseSettings);
        SetColorCommand = new RelayCommand<Brush>(SetColor);

        _valueRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _valueRefreshTimer.Tick += (_, _) => ValueRefresh?.Invoke(this, EventArgs.Empty);
        _valueRefreshTimer.Start();

        ConnectionManager.Instance.PropertyChanged += OnConnectionManagerPropertyChanged;
        Rebuild();
    }

    private void OnConnectionManagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectionManager.ConnectedDevices))
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        DisposeTiles(AnalogInputs);
        DisposeTiles(DigitalInputs);
        DisposeTiles(DigitalOutputs);

        var device = ConnectionManager.Instance.ConnectedDevices.FirstOrDefault();
        HasConnectedDevice = device != null;
        DeviceName = device?.Name ?? "";

        if (device == null)
        {
            RecomputeCounts();
            return;
        }

        foreach (var channel in device.DataChannels.NaturalOrderBy(c => c.Name))
        {
            var tile = new ChannelTileViewModel(channel, this);
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
}
