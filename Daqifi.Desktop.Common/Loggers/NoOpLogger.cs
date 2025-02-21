using System;

namespace Daqifi.Desktop.Common.Loggers
{
    public class NoOpLogger : IAppLogger
    {
        public void Information(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
        public void Error(Exception ex, string message) { }
    }
} 