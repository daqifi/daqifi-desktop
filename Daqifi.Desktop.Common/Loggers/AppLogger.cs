using NLog;
using NLog.Config;
using NLog.Targets;
using Bugsnag;
using System.Reflection;

namespace Daqifi.Desktop.Common.Loggers
{
    public class AppLogger : IAppLogger
    {
        #region Private Data
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly Client _client;
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
            fileTarget.FileName = "${specialfolder:folder=CommonApplicationData}\\DAQifi\\Logs\\DAQifiAppLog.log";
            fileTarget.Layout = "${longdate} LEVEL=${level:upperCase=true}: ${message}${newline} (${stacktrace}) ${exception:format=tostring} ${newline}";
            fileTarget.KeepFileOpen = false;

            // Setup Archiving
            fileTarget.ArchiveFileName = "${specialfolder:folder=CommonApplicationData}\\DAQifi\\Logs\\DAQifiAppLog.{#}.log";

            // Archive the log if it gets above 10MB
            fileTarget.ArchiveAboveSize = 10000000;
            fileTarget.ArchiveNumbering = ArchiveNumberingMode.Sequence;

            // Keep a maximum of 5 archives
            fileTarget.MaxArchiveFiles = 5;

            // Step 4. Define rules
            var rule = new LoggingRule("*", LogLevel.Debug, fileTarget);
            config.LoggingRules.Add(rule);

            // Step 5. Activate the configuration
            LogManager.Configuration = config;

            var configuration = new Configuration("899ecd666668c33e02cc5adc651a11b8")
            {
                AppVersion = Assembly.GetEntryAssembly().GetName().Version.ToString()

            };
            _client = new Client(configuration);
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
            _client.Notify(new Exception(message), Severity.Error);
        }

        public void Error(Exception ex, string message)
        {
            _logger.Error(ex, message);
            _client.Notify(ex, Severity.Error);
        }
        #endregion
    }
}
