using Daqifi.Desktop.DataModel.Channel;
using System.ComponentModel;

namespace Daqifi.Desktop.Channel;

public class Channel : IChannel, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public int ID { get; set; }
    public string Name { get; set; }
    public int Index { get; set; }
    public double OutputValue { get; set; }

    public ChannelType Type { get; set; }

    public ChannelDirection Direction { get; set; }

    public string TypeString { get; set; }

    public string ScaleExpression { get; set; }
    public System.Windows.Media.Brush ChannelColorBrush { get; set; }
    public bool IsBidirectional { get; set; }
    public bool IsOutput { get; set; }
    public bool HasAdc { get; set; }
    public bool IsActive { get; set; }

    public bool IsDigital { get; set; }

    public bool IsAnalog { get; set; }

    public bool IsDigitalOn { get; set; }
    public bool IsScalingActive { get; set; }
    public bool HasValidExpression { get; set; }
    public DataSample ActiveSample { get; set; }
    public string DeviceName { get ; set ; }
    public string DeviceSerialNo { get ; set; }

    private bool _isVisibleForPlot = true;
    public bool IsVisibleForPlot
    {
        get => _isVisibleForPlot;
        set
        {
            if (_isVisibleForPlot != value)
            {
                _isVisibleForPlot = value;
                OnPropertyChanged(nameof(IsVisibleForPlot));
            }
        }
    }

    public event OnChannelUpdatedHandler OnChannelUpdated;

    public void NotifyChannelUpdated(object sender, DataSample e)
    {
        OnChannelUpdated?.Invoke(sender, e);
    }

    public void SetColor(System.Windows.Media.Brush color)
    {
        color = ChannelColorBrush;
    }
}