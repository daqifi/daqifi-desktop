using Daqifi.Desktop.Configuration;
using Daqifi.Desktop.DataModel.Channel;

namespace Daqifi.Desktop.Channel
{
    public delegate void OnChannelUpdatedHandler(object sender, DataSample e);

    public interface IChannel : IColorable
    {
        #region Properties
        int ID { get; set; }
        string Name { get; set; }
        string DeviceName { get; set; }
        string DeviceSerialNo { get; set; }
        int Index { get; set; }
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
        bool IsScalingActive { get; set; }
        bool HasValidExpression { get; set; }
        DataSample ActiveSample { get; set; }
        #endregion

        #region Events
        event OnChannelUpdatedHandler OnChannelUpdated;
        #endregion

        void NotifyChannelUpdated(object sender, DataSample e);
    }
}
