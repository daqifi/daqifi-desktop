using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Helpers;
using Brush = System.Windows.Media.Brush;
using ChannelDirection = Daqifi.Core.Channel.ChannelDirection;

namespace Daqifi.Desktop.ViewModels;

/// <summary>
/// View-model for a single channel tile. All type-specific presentation
/// (stripe, label) is computed from the channel's current state so a
/// bidirectional channel flipping direction updates its tile in place.
/// </summary>
public sealed class ChannelTileViewModel : ObservableObject, IDisposable
{
    private readonly ChannelsPaneViewModel? _parent;
    private readonly INotifyPropertyChanged? _channelNotifier;

    /// <summary>The underlying domain channel this tile represents.</summary>
    public IChannel Channel { get; }

    /// <summary>Display name of the channel.</summary>
    public string Name => Channel.Name;

    /// <summary>Human-readable type label ("ANALOG IN", "DIGITAL IN", "DIGITAL OUT").</summary>
    public string TypeLabel => Channel.IsAnalog
        ? "ANALOG IN"
        : IsDigitalOutput ? "DIGITAL OUT" : "DIGITAL IN";

    /// <summary>Whether the channel is currently streaming samples.</summary>
    public bool IsActive => Channel.IsActive;

    /// <summary>Whether PWM output is currently enabled on this channel.</summary>
    public bool IsPwmActive => Channel.IsDigital && Channel.IsPwmEnabled;

    /// <summary>
    /// Whether this tile shelves as a digital output: a digital channel in output
    /// direction, or one with PWM enabled — a PWM channel drives its pin regardless
    /// of the stored direction (issue #664).
    /// </summary>
    public bool IsDigitalOutput =>
        Channel.IsDigital && (Channel.Direction == ChannelDirection.Output || IsPwmActive);

    /// <summary>
    /// Whether the tile renders its quick drive toggle. Hidden while PWM runs —
    /// the hardware ignores digital state writes on a PWM-active channel.
    /// </summary>
    public bool ShowDriveToggle => IsDigitalOutput && !IsPwmActive;

    /// <summary>
    /// Whether the tile should render its value line. Input tiles show live
    /// values only while streaming; output tiles always show the commanded
    /// state, because the pin is driven regardless of streaming activation.
    /// </summary>
    public bool ShowValue => IsActive || IsDigitalOutput;

    /// <summary>Type-coded stripe color — analog, digital in, or digital out.</summary>
    public Brush StripeBrush => Channel.IsAnalog
        ? AnalogAccent
        : IsDigitalOutput ? DigitalOutAccent : DigitalInAccent;

    /// <summary>Background color for the tile, depending on active state.</summary>
    public Brush TileBackground => IsActive ? TileBrushes.SurfaceActive : TileBrushes.SurfaceRaised;

    /// <summary>Border color for the tile — stripe color when active, dim otherwise.</summary>
    public Brush TileBorderBrush => IsActive ? StripeBrush : TileBrushes.BorderDim;

    /// <summary>
    /// Formatted value line: the commanded duty for PWM-active channels (issue #664),
    /// the last commanded state for digital outputs (issue #663), the live streamed
    /// value for inputs, or null when hidden.
    /// </summary>
    public string? Value
    {
        get
        {
            if (IsPwmActive) return $"PWM {Channel.PwmDutyCyclePercent}%";
            if (IsDigitalOutput) return Channel.IsDigitalOn ? "HIGH" : "LOW";
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

    /// <summary>
    /// Creates a tile bound to the given channel and parent pane. A null
    /// parent (unit tests only) skips the shared value-refresh subscription.
    /// </summary>
    public ChannelTileViewModel(
        IChannel channel,
        ChannelsPaneViewModel? parent,
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
        if (_parent != null)
        {
            _parent.ValueRefresh += OnValueRefresh;
        }
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
            OnPropertyChanged(nameof(ShowValue));
            OnPropertyChanged(nameof(Value));
        }
        else if (e.PropertyName == nameof(IChannel.Name))
        {
            OnPropertyChanged(nameof(Name));
        }
        else if (e.PropertyName == nameof(IChannel.IsDigitalOn) ||
                 e.PropertyName == nameof(IChannel.PwmDutyCyclePercent))
        {
            OnPropertyChanged(nameof(Value));
        }
        else if (e.PropertyName == nameof(IChannel.Direction) ||
                 e.PropertyName == nameof(IChannel.IsOutput) ||
                 e.PropertyName == nameof(IChannel.IsPwmEnabled))
        {
            OnPropertyChanged(nameof(TypeLabel));
            OnPropertyChanged(nameof(StripeBrush));
            OnPropertyChanged(nameof(TileBorderBrush));
            OnPropertyChanged(nameof(IsPwmActive));
            OnPropertyChanged(nameof(IsDigitalOutput));
            OnPropertyChanged(nameof(ShowDriveToggle));
            OnPropertyChanged(nameof(ShowValue));
            OnPropertyChanged(nameof(Value));
            _parent?.RequestSectionReshuffle();
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
        if (_parent != null)
        {
            _parent.ValueRefresh -= OnValueRefresh;
        }
    }

    private static readonly Brush AnalogAccent = TileBrushes.Frozen("#4A9EFF");
    private static readonly Brush DigitalInAccent = TileBrushes.Frozen("#4ADE80");
    private static readonly Brush DigitalOutAccent = TileBrushes.Frozen("#F59E0B");
}
