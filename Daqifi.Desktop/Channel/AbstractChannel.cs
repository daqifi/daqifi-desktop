using Daqifi.Desktop.DataModel.Channel;
using Daqifi.Desktop.Device;
using NCalc;
using System.ComponentModel.DataAnnotations.Schema;
using Brush = System.Windows.Media.Brush;


namespace Daqifi.Desktop.Channel;

public abstract class AbstractChannel : ObservableObject, IChannel
{
    #region Private Data
    private string _name;
    private double _outputValue;
    private ChannelDirection _direction = ChannelDirection.Unknown;
    private Brush _channelColorBrush;
    private bool _isOutput;
    private bool _hasAdc;
    private bool _isDigitalOn;
    private string _adcMode;
    private DataSample _activeSample;
    protected IStreamingDevice _owner;
    private string _scaledExpression;
    private bool _hasValidExpression;
    private bool _isScalingActive;
    #endregion

    #region Properties
    public int ID { get; set; }

    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            NotifyPropertyChanged("Name");
        }
    }
    public int Index { get; set; }

    public string DeviceName { get; set; }
    public string DeviceSerialNo { get; set; }

    public double OutputValue
    {
        get => _outputValue;
        set
        {
            if (Direction != ChannelDirection.Output) { return; }
            _outputValue = value;
            _owner.SetChannelOutputValue(this, value);
            NotifyPropertyChanged("OutputValue");
        }
    }

    [NotMapped]
    public List<string> AdcModes { get; } = new List<string> { "Single-Ended", "Differential" };

    public abstract ChannelType Type { get; }

    [NotMapped]
    public ChannelDirection Direction
    {
        get => _direction;
        set
        {
            if (_direction == value) { return; }

            if (_direction == ChannelDirection.Unknown)
            {
                _direction = value;
                NotifyPropertyChanged("Direction");
                return;
            }

            _direction = value;
            _owner.SetChannelDirection(this, value);
            NotifyPropertyChanged("Direction");
        }
    }

    [NotMapped]
    public bool IsBidirectional { get; set; }

    [NotMapped]
    public bool IsOutput
    {
        get => _isOutput;
        set
        {
            _isOutput = value;

            Direction = _isOutput ? ChannelDirection.Output : ChannelDirection.Input;
            NotifyPropertyChanged("IsOutput");
            NotifyPropertyChanged("TypeString");
        }
    }

    [NotMapped]
    public bool HasAdc
    {
        get => _hasAdc;
        set
        {
            _hasAdc = value;
            NotifyPropertyChanged("HasAdc");
        }
    }

    [NotMapped]
    public Brush ChannelColorBrush
    {
        get => _channelColorBrush;
        set
        {
            _channelColorBrush = value;
            _channelColorBrush.Freeze();
            NotifyPropertyChanged("ChannelColorBrush");
        }
    }

    [NotMapped]
    public bool IsActive { get; set; }

    [NotMapped]
    public abstract bool IsDigital { get; }

    [NotMapped]
    public bool IsDigitalOn
    {
        get => _isDigitalOn;
        set
        {
            _isDigitalOn = value;
            _owner.SetChannelOutputValue(this, value ? 1 : 0);
            NotifyPropertyChanged("IsDigitalOn");
        }
    }

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

            if (string.IsNullOrWhiteSpace(_scaledExpression)) { return; }

            Expression = new Expression(_scaledExpression)
            {
                Parameters = { ["x"] = 1 }
            };

            try
            {
                Expression.Evaluate();
                HasValidExpression = true;
            }
            catch (Exception ex)
            {
                HasValidExpression = false;
            }
        }
    }

    [NotMapped]
    public bool IsScalingActive
    {
        get => _isScalingActive;
        set
        {
            _isScalingActive = value;
            NotifyPropertyChanged("IsScalingActive");
        }
    }

    [NotMapped]
    public bool HasValidExpression
    {
        get => _hasValidExpression;
        set
        {
            _hasValidExpression = value;
            NotifyPropertyChanged("HasValidExpression");
        }
    }

    public Expression Expression { get; set; }

    public DataSample ActiveSample
    {
        get => _activeSample;
        set
        {
            _activeSample = value;
            if (Expression != null)
            {
                Expression.Parameters["x"] = _activeSample.Value;
                _activeSample.Value = (double)Expression.Evaluate();
            }
            NotifyChannelUpdated(this, _activeSample);
        }
    }
    #endregion

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
        if (!(obj is AbstractChannel channel)) { return false; }

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