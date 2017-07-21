using System;

namespace Daqifi.Desktop.Loggers
{
    public interface IAppLogger
    {
        void Information(string message);
        void Warning(string message);
        void Error(string message);
        void Error(Exception ex, string message);
    }
}
