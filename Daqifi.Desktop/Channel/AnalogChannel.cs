using Daqifi.Desktop.Device;

namespace Daqifi.Desktop.Channel
{
    public class AnalogChannel : AbstractChannel
    {       
        #region Properties
        public override ChannelType Type
        {
            get { return ChannelType.Analog; }
        }

        public override bool IsAnalog
        {
            get { return true; }
        }

        public override bool IsDigital
        {
            get { return false; }
        }
        #endregion

        #region Constructors
        public AnalogChannel() { }

        public AnalogChannel(IDevice owner, string name, int channelID, ChannelDirection direction, bool isBidirectional)
        {
            _owner = owner;
            Name = name;
            Index = channelID;
            IsOutput = direction == ChannelDirection.Output;
            HasAdc = !IsOutput;
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
}
