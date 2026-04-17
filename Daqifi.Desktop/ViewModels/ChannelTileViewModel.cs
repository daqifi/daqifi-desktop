using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Daqifi.Desktop.Channel;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using ChannelDirection = Daqifi.Core.Channel.ChannelDirection;

namespace Daqifi.Desktop.ViewModels;

/// <summary>
/// View-model for a single channel tile. All type-specific presentation
/// (stripe, label) is computed from the channel's current state so a
/// bidirectional channel flipping direction updates its tile in place.
/// </summary>
public sealed class ChannelTileViewModel : ObservableObject, IDisposable
{
    private readonly ChannelsPaneViewModel _parent;
    private readonly INotifyPropertyChanged? _channelNotifier;

    /// <summary>The underlying domain channel this tile represents.</summary>
    public IChannel Channel { get; }

    /// <summary>Display name of the channel.</summary>
    public string Name => Channel.Name;

    /// <summary>Human-readable type label ("ANALOG IN", "DIGITAL IN", "DIGITAL OUT").</summary>
    public string TypeLabel => Channel.IsAnalog
        ? "ANALOG IN"
        : Channel.Direction == ChannelDirection.Output ? "DIGITAL OUT" : "DIGITAL IN";

    /// <summary>Whether the channel is currently streaming samples.</summary>
    public bool IsActive => Channel.IsActive;

    /// <summary>Type-coded stripe color — analog, digital in, or digital out.</summary>
    public Brush StripeBrush => Channel.IsAnalog
        ? AnalogAccent
        : Channel.Direction == ChannelDirection.Output ? DigitalOutAccent : DigitalInAccent;

    /// <summary>Background color for the tile, depending on active state.</summary>
    public Brush TileBackground => IsActive ? SurfaceActive : SurfaceRaised;

    /// <summary>Border color for the tile — stripe color when active, dim otherwise.</summary>
    public Brush TileBorderBrush => IsActive ? StripeBrush : BorderDim;

    /// <summary>Formatted live value, or null when the channel is inactive.</summary>
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

    /// <summary>
    /// Name of the device this channel belongs to. Only surfaced on the tile
    /// when the pane is showing more than one connected device.
    /// </summary>
    public string DeviceName { get; }

    /// <summary>
    /// Whether the tile should render its device label. True only when the
    /// parent pane has more than one connected device, so single-device
    /// sessions stay uncluttered.
    /// </summary>
    public bool ShowDeviceLabel { get; }

    /// <summary>Creates a tile bound to the given channel and parent pane.</summary>
    public ChannelTileViewModel(
        IChannel channel,
        ChannelsPaneViewModel parent,
        string deviceName,
        bool showDeviceLabel)
    {
        Channel = channel;
        _parent = parent;
        DeviceName = deviceName;
        ShowDeviceLabel = showDeviceLabel;

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
        else if (e.PropertyName == nameof(IChannel.Direction) ||
                 e.PropertyName == nameof(IChannel.IsOutput))
        {
            OnPropertyChanged(nameof(TypeLabel));
            OnPropertyChanged(nameof(StripeBrush));
            OnPropertyChanged(nameof(TileBorderBrush));
            _parent.RequestSectionReshuffle();
        }
    }

    private void OnValueRefresh(object? sender, EventArgs e)
    {
        if (IsActive) OnPropertyChanged(nameof(Value));
    }

    /// <summary>Detaches channel and parent-pane subscriptions.</summary>
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
