using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
using NCalc;
using System.ComponentModel.DataAnnotations.Schema;
using Brush = System.Windows.Media.Brush;
using CommunityToolkit.Mvvm.ComponentModel;
using ChannelDirection = Daqifi.Core.Channel.ChannelDirection;
using ChannelType = Daqifi.Core.Channel.ChannelType;

namespace Daqifi.Desktop.Channel;

public abstract partial class AbstractChannel : ObservableObject, IChannel
{
    #region Private Data
    private string _scaledExpression;
    private DataSample _activeSample;
    protected IStreamingDevice _owner;
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
    private Brush _channelColorBrush;

    [ObservableProperty]
    private bool _isOutput;

    [ObservableProperty]
    private bool _hasAdc;

    [ObservableProperty]
    private bool _isDigitalOn;

    [ObservableProperty]
    private bool _isScalingActive;

    [ObservableProperty]
    private bool _hasValidExpression;

    /// <summary>
    /// Gets the channel index. Implemented by derived classes to delegate to core.
    /// </summary>
    public abstract int Index { get; }

    public string DeviceName { get; set; }
    public string DeviceSerialNo { get; set; }

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
            _scaledExpression = value;

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

    public Expression Expression { get; set; }

    [ObservableProperty]
    private bool _isVisible = true;

    public DataSample ActiveSample
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
                    _activeSample.Value = Convert.ToDouble(Expression.Evaluate());
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
            _owner?.SetChannelOutputValue(this, value);
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
        _owner?.SetChannelOutputValue(this, value ? 1 : 0);
    }

    #region Events/Handlers
    public event OnChannelUpdatedHandler OnChannelUpdated;

    public void NotifyChannelUpdated(object sender, DataSample e)
    {
        OnChannelUpdated?.Invoke(sender, e);
    }
    #endregion

    #region Object overrides
    public override bool Equals(object obj)
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