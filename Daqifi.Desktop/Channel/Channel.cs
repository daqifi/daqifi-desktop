using System.ComponentModel.DataAnnotations.Schema;
using ChannelDirection = Daqifi.Core.Channel.ChannelDirection;
using ChannelType = Daqifi.Core.Channel.ChannelType;

namespace Daqifi.Desktop.Channel;

public class Channel : IChannel
{
    public int ID { get; set; }
    public string Name { get; set; }
    public int Index { get; set; }
    public double OutputValue { get; set; }

    public ChannelType Type { get; init; }

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

    /// <summary>
    /// Gets or sets whether this channel's hardware supports PWM output. Runtime device
    /// state only — not persisted with a logging session (this class is an EF entity).
    /// </summary>
    [NotMapped]
    public bool IsPwmCapable { get; set; }

    /// <summary>
    /// Gets or sets whether PWM output is enabled. Runtime device state only — not
    /// persisted with a logging session.
    /// </summary>
    [NotMapped]
    public bool IsPwmEnabled { get; set; }

    /// <summary>
    /// Gets or sets the PWM duty cycle in whole percent. Runtime device state only —
    /// not persisted with a logging session.
    /// </summary>
    [NotMapped]
    public int PwmDutyCyclePercent { get; set; }

    public bool IsScalingActive { get; set; }
    public bool HasValidExpression { get; set; }
    public DataSample ActiveSample { get; set; }
    public bool IsVisible { get; set; } = true;
    public string DeviceName { get ; set ; }
    public string DeviceSerialNo { get ; set; }

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