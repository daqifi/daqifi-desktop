﻿using Daqifi.Desktop.Device;

namespace Daqifi.Desktop.Channel
{
    public class DigitalChannel : AbstractChannel
    {
        #region Properties
        public override ChannelType Type
        {
            get { return ChannelType.Digital; }
        }

        public override bool IsAnalog
        {
            get { return false; }
        }

        public override bool IsDigital
        {
            get { return true; }
        }
        #endregion

        #region Constructors
        public DigitalChannel() { }

        public DigitalChannel(IDevice owner, string name, int channelId, ChannelDirection direction, bool isBidirectional)
        {
            _owner = owner;
            Name = name;
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
}
