using NLog;
using NLog.Config;
using NLog.Targets;
using System;

namespace Daqifi.Desktop.Loggers
{
    public class AppLogger : IAppLogger
    {
        #region Private Data
        private NLog.Logger _logger = LogManager.GetCurrentClassLogger();
        #endregion

        #region Singleton Constructor / Initalization

        public static AppLogger Instance { get; } = new AppLogger();

        private AppLogger()
        {
            // Step 1. Create configuration object 
            var config = new LoggingConfiguration();

            // Step 2. Create targets and add them to the configuration 
            var fileTarget = new FileTarget();
            config.AddTarget("file", fileTarget);

            // Step 3. Set target properties 
            fileTarget.CreateDirs = true;
            fileTarget.FileName = "${specialfolder:folder=CommonApplicationData}\\DAQifi\\Logs\\nlog.txt.${shortdate}.log";
            fileTarget.Layout = "${longdate} LEVEL=${level:upperCase=true}: ${message}${newline} (${stacktrace}) ${exception:format=tostring} ${newline}";
            fileTarget.KeepFileOpen = false;

            // Step 4. Define rules
            var rule = new LoggingRule("*", LogLevel.Debug, fileTarget);
            config.LoggingRules.Add(rule);

            // Step 5. Activate the configuration
            LogManager.Configuration = config;
        }

        #endregion

        #region Logger Methods
        public void Information(string message)
        {
            _logger.Info(message);
        }

        public void Warning(string message)
        {
            _logger.Warn(message);
        }

        public void Error(string message)
        {
            _logger.Error(message);
        }

        public void Error(Exception ex, string message)
        {
            _logger.Error(ex, message);
        }
        #endregion
    }
}
