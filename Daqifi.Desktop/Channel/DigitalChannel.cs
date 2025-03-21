using Daqifi.Desktop.DataModel.Channel;
using Daqifi.Desktop.Device;

namespace Daqifi.Desktop.Channel;

public class DigitalChannel : AbstractChannel
{
    #region Properties
    public override ChannelType Type => ChannelType.Digital;

    public override bool IsAnalog => false;

    public override bool IsDigital => true;

    #endregion

    #region Constructors
    public DigitalChannel() { }

    public DigitalChannel(IStreamingDevice owner, string name, int channelId, ChannelDirection direction, bool isBidirectional)
    {
        _owner = owner;
        Name = name;
        DeviceName = owner.DevicePartNumber;
        DeviceSerialNo=owner.DeviceSerialNo;
        Index = channelId;
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