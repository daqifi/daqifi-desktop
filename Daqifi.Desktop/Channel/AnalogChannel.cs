using Daqifi.Desktop.DataModel.Channel;
using Daqifi.Desktop.Device;

namespace Daqifi.Desktop.Channel
{
    public class AnalogChannel : AbstractChannel
    {       
        #region Properties
        public override ChannelType Type => ChannelType.Analog;

        public override bool IsAnalog => true;

        public override bool IsDigital => false;

        public float CalibrationBValue { get; set; }

        public float CalibrationMValue { get; set; }

        public float InternalScaleMValue { get; set; }

        public float PortRange { get; set; }

        public uint Resolution { get; set; }

        #endregion

        #region Constructors
        public AnalogChannel() { }

        public AnalogChannel(IStreamingDevice owner, string name, int channelId, ChannelDirection direction, bool isBidirectional, float calibrationBValue, float calibrationMValue, float interalScaleMValue, float portRange, uint resolution)
        {
            _owner = owner;
            Name = name;
            Index = channelId;
            IsOutput = direction == ChannelDirection.Output;
            HasAdc = !IsOutput;
            IsBidirectional = isBidirectional;
            ChannelColorBrush = ChannelColorManager.Instance.NewColor();
            CalibrationBValue = calibrationBValue;
            CalibrationMValue = calibrationMValue;
            InternalScaleMValue = interalScaleMValue;
            PortRange = portRange;
            Resolution = resolution;
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
