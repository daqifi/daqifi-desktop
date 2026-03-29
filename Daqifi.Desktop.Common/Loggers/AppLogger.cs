using NLog;
using NLog.Config;
using NLog.Targets;
using Sentry;
using Sentry.NLog;
using System.Configuration;
using System.Reflection;

namespace Daqifi.Desktop.Common.Loggers;

public class AppLogger : IAppLogger
{
    #region Private Data
    private readonly Logger? _logger;
    private IDisposable? _sentryDisposable;
    private static readonly NoOpLogger NoOpLogger = new();
    private static readonly bool IsTestMode = IsRunningInTestEnvironment();
    #endregion

    public static AppLogger Instance { get; } = new();

    private static bool IsRunningInTestEnvironment()
    {
        try
        {
            // Check if we're running under a test runner by looking for test assemblies
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var testAssemblies = new[] { "Microsoft.VisualStudio.TestPlatform", "nunit.framework", "xunit" };

            return assemblies.Any(a => testAssemblies.Any(t => a.FullName?.Contains(t) == true));
        }
        catch
        {
            // If anything goes wrong checking the assemblies, assume we're not in test mode
            return false;
        }
    }

    private AppLogger()
    {
        if (IsTestMode)
        {
            // In test mode, don't initialize real logging
            _logger = null;
            _sentryDisposable = null;
            return;
        }

        // Step 1. Create configuration object
        var config = new LoggingConfiguration();

        // Step 2. Create targets and add them to the configuration
        var fileTarget = new FileTarget();
        config.AddTarget("file", fileTarget);

        // Step 3. Set target properties
        fileTarget.CreateDirs = true;
        fileTarget.FileName = @"${specialfolder:folder=CommonApplicationData}\DAQifi\Logs\DAQifiAppLog.log";
        fileTarget.Layout = "${longdate} LEVEL=${level:upperCase=true}: ${message}${newline} (${stacktrace}) ${exception:format=tostring} ${newline}";
        fileTarget.KeepFileOpen = false;

        // Setup Archiving
        fileTarget.ArchiveFileName = @"${specialfolder:folder=CommonApplicationData}\DAQifi\Logs\DAQifiAppLog.{#}.log";

        // Archive the log if it gets above 10MB
        fileTarget.ArchiveAboveSize = 10000000;
        fileTarget.ArchiveSuffixFormat = "{#}";

        // Keep a maximum of 5 archives
        fileTarget.MaxArchiveFiles = 5;

        // Step 4. Add Sentry NLog target so errors are forwarded to Sentry automatically
        var sentryTarget = new SentryTarget
        {
            Name = "sentry",
            Layout = "${message}",
            MinimumEventLevel = "Error"
        };
        config.AddTarget("sentry", sentryTarget);

        // Step 5. Define rules
        var rule = new LoggingRule("*", LogLevel.Debug, fileTarget);
        config.LoggingRules.Add(rule);
        var sentryRule = new LoggingRule("*", LogLevel.Error, sentryTarget);
        config.LoggingRules.Add(sentryRule);

        // Step 6. Activate the configuration
        LogManager.Configuration = config;

        _logger = LogManager.GetCurrentClassLogger();

        // Step 7. Initialize Sentry SDK
        var dsn = ConfigurationManager.AppSettings["SentryDsn"] ?? string.Empty;
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";

        _sentryDisposable = SentrySdk.Init(options =>
        {
            options.Dsn = dsn;
            options.Release = version;
            options.AutoSessionTracking = true;
            options.IsGlobalModeEnabled = true;
        });
    }

    #region Logger Methods
    /// <summary>
    /// Logs an informational message.
    /// </summary>
    public void Information(string message)
    {
        if (IsTestMode)
        {
            NoOpLogger.Information(message);
            return;
        }
        _logger.Info(message);
    }

    /// <summary>
    /// Logs a warning message.
    /// </summary>
    public void Warning(string message)
    {
        if (IsTestMode)
        {
            NoOpLogger.Warning(message);
            return;
        }
        _logger.Warn(message);
    }

    /// <summary>
    /// Logs an error message and captures it in Sentry.
    /// </summary>
    public void Error(string message)
    {
        if (IsTestMode)
        {
            NoOpLogger.Error(message);
            return;
        }
        _logger.Error(message);
        SentrySdk.CaptureException(new Exception(message));
    }

    /// <summary>
    /// Logs an exception with a descriptive message and captures it in Sentry.
    /// </summary>
    public void Error(Exception ex, string message)
    {
        if (IsTestMode)
        {
            NoOpLogger.Error(ex, message);
            return;
        }
        _logger.Error(ex, message);
        SentrySdk.CaptureException(ex);
    }
    #endregion
}
