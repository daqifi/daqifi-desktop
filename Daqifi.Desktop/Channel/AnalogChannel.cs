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
    public new string Name
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
    public new ChannelDirection Direction
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
    /// Gets or sets the channel index. Delegates to core ChannelNumber to avoid duplication.
    /// </summary>
    public new int Index
    {
        get => _coreChannel.ChannelNumber;
        // Index is read-only in core (ChannelNumber), so no setter
    }

    /// <summary>
    /// Gets or sets whether the channel is active. Synchronized with core IsEnabled.
    /// </summary>
    public new bool IsActive
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

    public float CalibrationBValue
    {
        get => (float)_coreChannel.CalibrationB;
        set => _coreChannel.CalibrationB = value;
    }

    public float CalibrationMValue
    {
        get => (float)_coreChannel.CalibrationM;
        set => _coreChannel.CalibrationM = value;
    }

    public float InternalScaleMValue
    {
        get => (float)_coreChannel.InternalScaleM;
        set => _coreChannel.InternalScaleM = value;
    }

    public float PortRange
    {
        get => (float)_coreChannel.PortRange;
        set => _coreChannel.PortRange = value;
    }

    public uint Resolution => _coreChannel.Resolution;

    /// <summary>
    /// Gets the core channel for device communication
    /// </summary>
    internal Daqifi.Core.Channel.IAnalogChannel CoreChannel => _coreChannel;

    #endregion

    #region Constructors
    public AnalogChannel(IStreamingDevice owner, string name, int channelId, ChannelDirection direction, bool isBidirectional, float calibrationBValue, float calibrationMValue, float interalScaleMValue, float portRange, uint resolution)
    {
        _owner = owner;

        // Create core channel for device communication
        _coreChannel = new Daqifi.Core.Channel.AnalogChannel(channelId, resolution)
        {
            Name = name,
            Direction = direction,
            CalibrationB = calibrationBValue,
            CalibrationM = calibrationMValue,
            InternalScaleM = interalScaleMValue,
            PortRange = portRange,
            IsEnabled = false
        };

        // Set desktop-specific properties (not in core)
        DeviceName = owner.DevicePartNumber;
        DeviceSerialNo = owner.DeviceSerialNo;
        IsBidirectional = isBidirectional;
        ChannelColorBrush = ChannelColorManager.Instance.NewColor();

        // Initialize derived desktop state based on core
        IsOutput = direction == ChannelDirection.Output;
        HasAdc = !IsOutput;
    }

    #endregion

    #region Public Methods
    /// <summary>
    /// Gets the scaled value using core's calibration formula
    /// </summary>
    public double GetScaledValue(int rawValue)
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