using Daqifi.Desktop.DataModel.Channel;
using Daqifi.Desktop.Device;

namespace Daqifi.Desktop.Channel;

public class DigitalChannel : AbstractChannel
{
    #region Private Fields
    /// <summary>
    /// Core channel implementation handling device communication
    /// </summary>
    private readonly Daqifi.Core.Channel.IDigitalChannel _coreChannel;
    #endregion

    #region Properties
    public override ChannelType Type => ChannelType.Digital;

    public override bool IsAnalog => false;

    public override bool IsDigital => true;

    /// <summary>
    /// Gets the core channel for device communication
    /// </summary>
    internal Daqifi.Core.Channel.IDigitalChannel CoreChannel => _coreChannel;

    #endregion

    #region Constructors
    public DigitalChannel(IStreamingDevice owner, string name, int channelId, ChannelDirection direction, bool isBidirectional)
    {
        _owner = owner;

        // Create core channel for device communication
        _coreChannel = new Daqifi.Core.Channel.DigitalChannel(channelId)
        {
            Name = name,
            Direction = (Daqifi.Core.Channel.ChannelDirection)(int)direction,
            IsEnabled = false
        };

        // Set desktop-specific properties
        Name = name;
        DeviceName = owner.DevicePartNumber;
        DeviceSerialNo=owner.DeviceSerialNo;
        Index = channelId;
        Direction = direction;
        IsOutput = direction == ChannelDirection.Output;
        IsBidirectional = isBidirectional;
        ChannelColorBrush = ChannelColorManager.Instance.NewColor();
    }
    #endregion

    #region Object Overrides
    public override string ToString()
    {
        return Name;
    }
    #endregion
}