using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Daqifi.Desktop.Channel;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using ChannelDirection = Daqifi.Core.Channel.ChannelDirection;

namespace Daqifi.Desktop.ViewModels;

public sealed class ChannelTileViewModel : ObservableObject, IDisposable
{
    private readonly ChannelsPaneViewModel _parent;
    private readonly Brush _stripeBrush;
    private readonly string _typeLabel;
    private readonly INotifyPropertyChanged? _channelNotifier;

    public IChannel Channel { get; }
    public string Name => Channel.Name;
    public string TypeLabel => _typeLabel;
    public bool IsActive => Channel.IsActive;
    public Brush StripeBrush => _stripeBrush;
    public Brush TileBackground => IsActive ? SurfaceActive : SurfaceRaised;
    public Brush TileBorderBrush => IsActive ? _stripeBrush : BorderDim;

    public string? Value
    {
        get
        {
            if (!IsActive) return null;
            var sample = Channel.ActiveSample;
            if (sample == null) return "—";
            if (Channel.IsAnalog) return $"{sample.Value:F3} V";
            return sample.Value > 0.5 ? "HIGH" : "LOW";
        }
    }

    public ChannelTileViewModel(IChannel channel, ChannelsPaneViewModel parent)
    {
        Channel = channel;
        _parent = parent;

        if (channel.IsAnalog)
        {
            _stripeBrush = AnalogAccent;
            _typeLabel = "ANALOG IN";
        }
        else if (channel.Direction == ChannelDirection.Output)
        {
            _stripeBrush = DigitalOutAccent;
            _typeLabel = "DIGITAL OUT";
        }
        else
        {
            _stripeBrush = DigitalInAccent;
            _typeLabel = "DIGITAL IN";
        }

        _channelNotifier = channel as INotifyPropertyChanged;
        if (_channelNotifier != null)
        {
            _channelNotifier.PropertyChanged += OnChannelPropertyChanged;
        }
        parent.ValueRefresh += OnValueRefresh;
    }

    private void OnChannelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // ActiveSample fires from the streaming thread; handle it via the UI-thread
        // ValueRefresh tick instead. Only react here to properties set from the UI thread.
        if (e.PropertyName == nameof(IChannel.IsActive))
        {
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(TileBackground));
            OnPropertyChanged(nameof(TileBorderBrush));
            OnPropertyChanged(nameof(Value));
        }
        else if (e.PropertyName == nameof(IChannel.Name))
        {
            OnPropertyChanged(nameof(Name));
        }
    }

    private void OnValueRefresh(object? sender, EventArgs e)
    {
        if (IsActive) OnPropertyChanged(nameof(Value));
    }

    public void Dispose()
    {
        if (_channelNotifier != null)
        {
            _channelNotifier.PropertyChanged -= OnChannelPropertyChanged;
        }
        _parent.ValueRefresh -= OnValueRefresh;
    }

    private static readonly Brush SurfaceRaised = MakeBrush("#171A20");
    private static readonly Brush SurfaceActive = MakeBrush("#1E2530");
    private static readonly Brush BorderDim = MakeBrush("#2A2F38");
    private static readonly Brush AnalogAccent = MakeBrush("#4A9EFF");
    private static readonly Brush DigitalInAccent = MakeBrush("#4ADE80");
    private static readonly Brush DigitalOutAccent = MakeBrush("#F59E0B");

    private static SolidColorBrush MakeBrush(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex)!;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
