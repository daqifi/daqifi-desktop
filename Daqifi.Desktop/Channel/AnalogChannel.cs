using Daqifi.Desktop.DataModel.Channel;
using Daqifi.Desktop.Device;

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

        // Set desktop-specific properties
        Name = name;
        DeviceName = owner.DevicePartNumber;
        DeviceSerialNo = owner.DeviceSerialNo;
        Index = channelId;
        Direction = direction;
        IsOutput = direction == ChannelDirection.Output;
        HasAdc = !IsOutput;
        IsBidirectional = isBidirectional;
        ChannelColorBrush = ChannelColorManager.Instance.NewColor();
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