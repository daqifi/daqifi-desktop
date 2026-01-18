using Daqifi.Desktop.Device;
using ChannelDirection = Daqifi.Core.Channel.ChannelDirection;
using ChannelType = Daqifi.Core.Channel.ChannelType;

namespace Daqifi.Desktop.Channel;

public class AnalogChannel : AbstractChannel
{
    #region Private Fields
    /// <summary>
    /// Core channel implementation handling device communication and scaling
    /// </summary>
    private readonly Daqifi.Core.Channel.IAnalogChannel _coreChannel;
    #endregion

    #region Properties
    public override ChannelType Type => ChannelType.Analog;

    public override bool IsAnalog => true;

    public override bool IsDigital => false;

    /// <summary>
    /// Gets or sets the channel name. Delegates to core channel to avoid duplication.
    /// </summary>
    public override string Name
    {
        get => _coreChannel.Name;
        set
        {
            if (_coreChannel.Name != value)
            {
                _coreChannel.Name = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the channel direction. Delegates to core channel to avoid duplication.
    /// </summary>
    public override ChannelDirection Direction
    {
        get => _coreChannel.Direction;
        set
        {
            if (_coreChannel.Direction != value)
            {
                _coreChannel.Direction = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TypeString));

                // Update derived state
                IsOutput = value == ChannelDirection.Output;
                HasAdc = !IsOutput;

                // Notify owner of direction change
                _owner?.SetChannelDirection(this, value);
            }
        }
    }

    /// <summary>
    /// Gets the channel index. Delegates to core ChannelNumber to avoid duplication.
    /// </summary>
    public override int Index => _coreChannel.ChannelNumber;

    /// <summary>
    /// Gets or sets whether the channel is active. Synchronized with core IsEnabled.
    /// </summary>
    public override bool IsActive
    {
        get => _coreChannel.IsEnabled;
        set
        {
            if (_coreChannel.IsEnabled != value)
            {
                _coreChannel.IsEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public double CalibrationBValue
    {
        get => _coreChannel.CalibrationB;
        set => _coreChannel.CalibrationB = value;
    }

    public double CalibrationMValue
    {
        get => _coreChannel.CalibrationM;
        set => _coreChannel.CalibrationM = value;
    }

    public double InternalScaleMValue
    {
        get => _coreChannel.InternalScaleM;
        set => _coreChannel.InternalScaleM = value;
    }

    public double PortRange
    {
        get => _coreChannel.PortRange;
        set => _coreChannel.PortRange = value;
    }

    public uint Resolution => _coreChannel.Resolution;

    /// <summary>
    /// Gets the core channel for device communication
    /// </summary>
    internal Daqifi.Core.Channel.IAnalogChannel CoreChannel => _coreChannel;

    #endregion

    #region Constructors
    /// <summary>
    /// Creates a new AnalogChannel by wrapping a Core channel instance.
    /// </summary>
    /// <param name="owner">The device that owns this channel.</param>
    /// <param name="coreChannel">The Core channel instance to wrap.</param>
    public AnalogChannel(IStreamingDevice owner, Daqifi.Core.Channel.IAnalogChannel coreChannel)
    {
        _owner = owner;
        _coreChannel = coreChannel;

        // Set desktop-specific properties (not in core)
        DeviceName = owner.DevicePartNumber;
        DeviceSerialNo = owner.DeviceSerialNo;
        IsBidirectional = false;
        ChannelColorBrush = ChannelColorManager.Instance.NewColor();

        // Initialize derived desktop state based on core
        IsOutput = coreChannel.Direction == ChannelDirection.Output;
        HasAdc = !IsOutput;
    }
    #endregion

    #region Internal Methods
    /// <summary>
    /// Gets the scaled value using core's calibration formula
    /// </summary>
    internal double GetScaledValue(int rawValue)
    {
        return _coreChannel.GetScaledValue(rawValue);
    }
    #endregion

    #region Object Overrides
    public override string ToString()
    {
        return Name;
    }
    #endregion
}