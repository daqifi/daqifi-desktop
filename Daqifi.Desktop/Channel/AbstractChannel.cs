using Daqifi.Desktop.Device;
using NCalc;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Windows.Media;

namespace Daqifi.Desktop.Channel
{
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
        protected IDevice _owner;
        private string _scaledExpression;
        private bool _hasValidExpression;
        private bool _isScalingActive;
        #endregion

        #region Properties
        public int ID { get; set; }
        public string Name
        {
            get { return _name; }
            set
            {
                _name = value;
                NotifyPropertyChanged("Name");
            }
        }
        public int Index { get; set; }

        public double OutputValue
        {
            get { return _outputValue; }
            set
            {
                if(Direction != ChannelDirection.Output) return;
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
            get { return _direction; }
            set 
            { 
                if(_direction == value)  return;

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
        public string AdcMode
        {
            get { return _adcMode; }
            set
            {
                if (value == AdcModes[0])
                {
                    _owner.SetAdcMode(this, Channel.AdcMode.SingleEnded);
                }
                else if (value == AdcModes[1])
                {
                    _owner.SetAdcMode(this, Channel.AdcMode.Differential);
                }
                else
                {
                    return;
                }
                _adcMode = value;
                NotifyPropertyChanged("AdcMode");
            }
        }

        [NotMapped]
        public bool IsBidirectional { get; set; }

        [NotMapped]
        public bool IsOutput
        {
            get { return _isOutput; }
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
            get { return _hasAdc; }
            set
            {
                _hasAdc = value;
                NotifyPropertyChanged("HasAdc");
            }
        }

        [NotMapped]
        public Brush ChannelColorBrush
        {
            get { return _channelColorBrush; }
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
            get { return _isDigitalOn; }
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
                string typeString = "";

                if (IsDigital) typeString = "Digital ";
                if (IsAnalog) typeString = "Analog ";

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
            get { return _scaledExpression; }
            set
             {
                _scaledExpression = value;

                if (string.IsNullOrWhiteSpace(_scaledExpression)) return;

                Expression = new Expression(_scaledExpression, EvaluateOptions.IgnoreCase);
                Expression.Parameters["x"] = 1;
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
            get { return _isScalingActive; }
            set
            {
                _isScalingActive = value;
                NotifyPropertyChanged("IsScalingActive");
            }
        }

        [NotMapped]
        public bool HasValidExpression
        {
            get { return _hasValidExpression; }
            set
            {
                _hasValidExpression = value;
                NotifyPropertyChanged("HasValidExpression");
            }
        }

        public Expression Expression { get; set; }

        public DataSample ActiveSample
        {
            get { return _activeSample; }
            set
            {
                _activeSample = value;
                if(Expression != null)
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
            if (obj == null) return false;

            var channel = obj as AbstractChannel;
            if (channel == null) return false;

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
}
