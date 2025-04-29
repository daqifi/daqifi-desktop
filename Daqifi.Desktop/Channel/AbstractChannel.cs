using Daqifi.Desktop.DataModel.Channel;
using Daqifi.Desktop.Device;
using NCalc;
using System.ComponentModel.DataAnnotations.Schema;
using Brush = System.Windows.Media.Brush;
using CommunityToolkit.Mvvm.ComponentModel;

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

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private double _outputValue;

    [ObservableProperty]
    private ChannelDirection _direction = ChannelDirection.Unknown;

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

    public int Index { get; set; }
    public string DeviceName { get; set; }
    public string DeviceSerialNo { get; set; }

    public abstract ChannelType Type { get; }

    [NotMapped]
    public bool IsBidirectional { get; set; }

    [NotMapped]
    public bool IsActive { get; set; }

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

            switch (Direction)
            {
                case ChannelDirection.Input:
                    typeString += "Input";
                    break;
                case ChannelDirection.Output:
                    typeString += "Output";
                    break;
                default:
                    typeString += "Unknown";
                    break;
            }

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
                    // Handle evaluation error, maybe log it or invalidate the sample?
                    // For now, we just skip scaling
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

    partial void OnDirectionChanged(ChannelDirection value)
    {
        if (Direction != ChannelDirection.Unknown && value != Direction)
        {
            _owner?.SetChannelDirection(this, value);
            OnPropertyChanged(nameof(TypeString));
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
    #endregion

    #region IColorable overrides
    public void SetColor(Brush color)
    {
        ChannelColorBrush = color;
    }
    #endregion
}