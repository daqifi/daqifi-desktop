using Daqifi.Desktop.Configuration;
using ChannelDirection = Daqifi.Core.Channel.ChannelDirection;
using ChannelType = Daqifi.Core.Channel.ChannelType;

namespace Daqifi.Desktop.Channel;

public delegate void OnChannelUpdatedHandler(object sender, DataSample e);

public interface IChannel : IColorable
{
    #region Properties
    int ID { get; set; }
    string Name { get; set; }
    string DeviceName { get; set; }
    string DeviceSerialNo { get; set; }
    int Index { get; }
    double OutputValue { get; set; }
    ChannelType Type { get; }
    ChannelDirection Direction { get; set; }
    string TypeString { get; }
    string ScaleExpression { get; set; }
    System.Windows.Media.Brush ChannelColorBrush { get; set; }
    bool IsBidirectional { get; set; }
    bool IsOutput { get; set; }
    bool HasAdc { get; set; }
    bool IsActive { get; set; }
    bool IsDigital { get; }
    bool IsAnalog { get; }
    bool IsDigitalOn { get; set; }

    /// <summary>
    /// Gets whether this channel's hardware supports PWM output. Populated from the
    /// firmware board mask via Core; false for analog channels.
    /// </summary>
    bool IsPwmCapable { get; }

    /// <summary>
    /// Gets or sets whether PWM output is enabled on this channel. Setting it commands
    /// the device through Core (duty → shared frequency → enable on the way up).
    /// </summary>
    bool IsPwmEnabled { get; set; }

    /// <summary>
    /// Gets or sets the PWM duty cycle in whole percent (1-100). While PWM is enabled the
    /// change is commanded to the device immediately; otherwise it is stored and applied
    /// on the next enable.
    /// </summary>
    int PwmDutyCyclePercent { get; set; }

    bool IsScalingActive { get; set; }
    bool HasValidExpression { get; set; }
    DataSample ActiveSample { get; set; }
    bool IsVisible { get; set; }
    #endregion

    #region Events
    event OnChannelUpdatedHandler OnChannelUpdated;
    #endregion

    void NotifyChannelUpdated(object sender, DataSample e);
}