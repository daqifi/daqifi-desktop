using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Device;

namespace Daqifi.Desktop.Logger
{
    public interface ILogger
    {
        void Log(DataSample dataSample);

        void Log(DeviceMessage dataSample);
    }
}
