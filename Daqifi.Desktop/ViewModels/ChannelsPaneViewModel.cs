using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Helpers;
using Daqifi.Desktop.Logger;
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

    public IRelayCommand<ChannelTileViewModel> ToggleChannelCommand { get; }
    public IRelayCommand<string> SelectAllCommand { get; }
    public IRelayCommand ClearAllCommand { get; }

    public ChannelsPaneViewModel()
    {
        ToggleChannelCommand = new RelayCommand<ChannelTileViewModel>(ToggleChannel);
        SelectAllCommand = new RelayCommand<string>(SelectAll);
        ClearAllCommand = new RelayCommand(ClearAll);

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
}
