using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
using NCalc;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using Brush = System.Windows.Media.Brush;
using CommunityToolkit.Mvvm.ComponentModel;
using ChannelDirection = Daqifi.Core.Channel.ChannelDirection;
using ChannelType = Daqifi.Core.Channel.ChannelType;

namespace Daqifi.Desktop.Channel;

public abstract partial class AbstractChannel : ObservableObject, IChannel
{
    #region Private Data
    private string _scaledExpression = string.Empty;
    private DataSample? _activeSample;
    private bool _suppressDigitalOutputCommand;
    #endregion

    #region Protected Properties
    /// <summary>
    /// The device that owns this channel. Null until the owning device hydrates its channel
    /// list, and on channels constructed for display only, so every use is null-conditional.
    /// </summary>
    protected IStreamingDevice? Owner { get; set; }
    #endregion

    #region Properties
    public int ID { get; set; }

    /// <summary>
    /// Gets or sets the channel name. Implemented by derived classes to delegate to core.
    /// </summary>
    public abstract string Name { get; set; }

    [ObservableProperty]
    private double _outputValue;

    /// <summary>
    /// Gets or sets the channel direction. Implemented by derived classes to delegate to core.
    /// </summary>
    public abstract ChannelDirection Direction { get; set; }

    [ObservableProperty]
    // Transparent until the channel is assigned a color by ChannelColorManager; renders
    // identically to the previous (null) default while keeping the IChannel contract non-null.
    private Brush _channelColorBrush = System.Windows.Media.Brushes.Transparent;

    [ObservableProperty]
    private bool _isOutput;

    [ObservableProperty]
    private bool _hasAdc;

    [ObservableProperty]
    private bool _isDigitalOn;

    /// <summary>
    /// Whether this channel's hardware supports PWM output. Only digital channels can be
    /// PWM-capable; <see cref="DigitalChannel"/> overrides with Core's board-mask answer.
    /// </summary>
    public virtual bool IsPwmCapable => false;

    /// <summary>
    /// Whether PWM output is enabled. No-op on channel types without PWM support;
    /// <see cref="DigitalChannel"/> overrides to command the device through Core.
    /// </summary>
    public virtual bool IsPwmEnabled
    {
        get => false;
        set { }
    }

    /// <summary>
    /// The PWM duty cycle in whole percent. No-op on channel types without PWM support;
    /// <see cref="DigitalChannel"/> overrides with Core-backed bookkeeping.
    /// </summary>
    public virtual int PwmDutyCyclePercent
    {
        get => 0;
        set { }
    }

    [ObservableProperty]
    private bool _isScalingActive;

    [ObservableProperty]
    private bool _hasValidExpression;

    /// <summary>
    /// Gets the channel index. Implemented by derived classes to delegate to core.
    /// </summary>
    public abstract int Index { get; }

    public string DeviceName { get; set; } = string.Empty;
    public string DeviceSerialNo { get; set; } = string.Empty;

    public abstract ChannelType Type { get; }

    [NotMapped]
    public bool IsBidirectional { get; set; }

    [NotMapped]
    public abstract bool IsActive { get; set; }

    [NotMapped]
    public abstract bool IsDigital { get; }

    [NotMapped]
    public abstract bool IsAnalog { get; }

    [NotMapped]
    public string TypeString
    {
        get
        {
            var typeString = "";

            if (IsDigital) { typeString = "Digital "; }
            if (IsAnalog) {typeString = "Analog "; }

            typeString += Direction switch
            {
                ChannelDirection.Input => "Input",
                ChannelDirection.Output => "Output",
                _ => "Unknown"
            };

            return typeString;
        }
    }

    [NotMapped]
    public string ScaleExpression
    {
        get => _scaledExpression;
        set
        {
            // The IChannel contract declares this non-nullable, but the scaling drawer can clear
            // the field; normalize to empty so the getter never hands back null.
            _scaledExpression = value ?? string.Empty;

            if (string.IsNullOrWhiteSpace(_scaledExpression))
            {
                HasValidExpression = false;
                Expression = null;
                return;
            }

            Expression = new Expression(_scaledExpression)
            {
                Parameters = { ["x"] = 1 }
            };

            try
            {
                Expression.Evaluate();
                HasValidExpression = true;
            }
            catch (Exception)
            {
                HasValidExpression = false;
                Expression = null;
            }
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The compiled NCalc scaling expression, or null when no valid expression is configured.
    /// </summary>
    public Expression? Expression { get; set; }

    [ObservableProperty]
    private bool _isVisible = true;

    public DataSample? ActiveSample
    {
        get => _activeSample;
        set
        {
            _activeSample = value;
            if (Expression != null && HasValidExpression && IsScalingActive && _activeSample != null)
            {
                try
                {
                    Expression.Parameters["x"] = _activeSample.Value;
                    // Invariant culture: this is a numeric transform feeding the plot and exported
                    // data, not user-facing display text, so it must not follow the machine locale.
                    var scaledValue = Convert.ToDouble(Expression.Evaluate(), CultureInfo.InvariantCulture);

                    if (double.IsFinite(scaledValue))
                    {
                        _activeSample.Value = scaledValue;
                    }
                    else
                    {
                        // A finite input can still yield a non-finite result WITHOUT throwing:
                        // a float divide-by-zero gives +/-Infinity and 0.0/0.0 gives NaN. Keep
                        // the raw value and disable scaling (mirroring the catch below) so
                        // Infinity/NaN never reaches the live plot or exported data.
                        AppLogger.Instance.Warning(
                            $"Expression produced a non-finite result for channel {Name}; scaling disabled.");
                        HasValidExpression = false;
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Instance.Warning($"Expression evaluation failed for channel {Name}: {ex.Message}");
                    HasValidExpression = false;
                }
            }

            if (_activeSample != null)
            {
                NotifyChannelUpdated(this, _activeSample);
            }
            OnPropertyChanged();
        }
    }
    #endregion

    partial void OnOutputValueChanged(double value)
    {
        if (Direction == ChannelDirection.Output)
        {
            Owner?.SetChannelOutputValue(this, value);
        }
    }

    partial void OnIsOutputChanged(bool value)
    {
        Direction = value ? ChannelDirection.Output : ChannelDirection.Input;
        OnPropertyChanged(nameof(TypeString));
    }

    partial void OnChannelColorBrushChanged(Brush value)
    {
        value?.Freeze();
    }

    partial void OnIsDigitalOnChanged(bool value)
    {
        if (_suppressDigitalOutputCommand)
        {
            return;
        }

        Owner?.SetChannelOutputValue(this, value ? 1 : 0);
    }

    /// <summary>
    /// Sets <see cref="IsDigitalOn"/> from already-known device/Core state — hydration,
    /// not a user action — so the change notifies the UI without re-issuing the output
    /// command that a user-driven change sends.
    /// </summary>
    protected void HydrateIsDigitalOn(bool value)
    {
        _suppressDigitalOutputCommand = true;
        try
        {
            IsDigitalOn = value;
        }
        finally
        {
            _suppressDigitalOutputCommand = false;
        }
    }

    #region Events/Handlers
    public event OnChannelUpdatedHandler? OnChannelUpdated;

    public void NotifyChannelUpdated(object sender, DataSample e)
    {
        OnChannelUpdated?.Invoke(sender, e);
    }
    #endregion

    #region Object overrides
    public override bool Equals(object? obj)
    {
        if (obj is not AbstractChannel channel) { return false; }

        return channel.Name == Name;
    }

    public override int GetHashCode()
    {
        return Name?.GetHashCode() ?? 0;
    }
    #endregion

    #region IColorable overrides
    public void SetColor(Brush color)
    {
        ChannelColorBrush = color;
    }
    #endregion
}