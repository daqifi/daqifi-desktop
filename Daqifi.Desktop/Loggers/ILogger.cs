using Daqifi.Desktop.Channel;

namespace Daqifi.Desktop.Logger
{
    public interface ILogger
    {
        void Log(DataSample dataSample);
    }
}
