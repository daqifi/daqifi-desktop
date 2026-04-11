using NLog;
using NLog.Config;
using NLog.Targets;
using Sentry;
using System.Configuration;
using System.Reflection;

namespace Daqifi.Desktop.Common.Loggers;

public class AppLogger : IAppLogger
{
    #region Private Data
    private readonly Logger? _logger;
    private IDisposable? _sentryDisposable;
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

        // Step 4. Define rules
        var rule = new LoggingRule("*", LogLevel.Debug, fileTarget);
        config.LoggingRules.Add(rule);

        // Step 5. Activate the configuration
        LogManager.Configuration = config;

        _logger = LogManager.GetCurrentClassLogger();

        // Step 6. Initialize Sentry SDK — explicit CaptureException calls in Error() are the
        // sole capture path; SentryTarget is intentionally omitted to avoid double-reporting.
        var dsn = ConfigurationManager.AppSettings["SentryDsn"];
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";

        if (string.IsNullOrWhiteSpace(dsn))
        {
            _logger.Warn("SentryDsn not found in AppSettings — Sentry error reporting is disabled");
            return;
        }

        _sentryDisposable = SentrySdk.Init(options =>
        {
            options.Dsn = dsn;
            options.Release = version;
            options.AutoSessionTracking = true;
            options.IsGlobalModeEnabled = true;
        });
    }

    #region Logger Methods
    public void Information(string message)
    {
        _logger?.Info(message);
    }

    public void Warning(string message)
    {
        _logger?.Warn(message);
    }

    public void Error(string message)
    {
        _logger?.Error(message);
        SentrySdk.CaptureException(new Exception(message));
    }

    public void Error(Exception ex, string message)
    {
        _logger?.Error(ex, message);
        SentrySdk.CaptureException(ex, scope => scope.SetExtra("message", message));
    }

    /// <inheritdoc />
    public void AddBreadcrumb(string category, string message, BreadcrumbLevel level = BreadcrumbLevel.Info)
    {
        SentrySdk.AddBreadcrumb(
            message: message,
            category: category,
            level: MapBreadcrumbLevel(level));
    }

    /// <inheritdoc />
    public void SetDeviceContext(string model, string serialNumber, string firmwareVersion, string connectionType, int activeChannels)
    {
        SentrySdk.ConfigureScope(scope =>
        {
            scope.SetTag("daqifi.device_model", model ?? "unknown");
            scope.SetTag("daqifi.serial_number", serialNumber ?? "unknown");
            scope.SetTag("daqifi.firmware_version", firmwareVersion ?? "unknown");
            scope.SetTag("daqifi.connection_type", connectionType);
            scope.SetTag("daqifi.active_channels", activeChannels.ToString(System.Globalization.CultureInfo.InvariantCulture));
        });
    }

    /// <inheritdoc />
    public void ClearDeviceContext()
    {
        SentrySdk.ConfigureScope(scope =>
        {
            scope.UnsetTag("daqifi.device_model");
            scope.UnsetTag("daqifi.serial_number");
            scope.UnsetTag("daqifi.firmware_version");
            scope.UnsetTag("daqifi.connection_type");
            scope.UnsetTag("daqifi.active_channels");
        });
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        SentrySdk.FlushAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        _sentryDisposable?.Dispose();
    }

    private static Sentry.BreadcrumbLevel MapBreadcrumbLevel(BreadcrumbLevel level)
    {
        return level switch
        {
            BreadcrumbLevel.Debug => Sentry.BreadcrumbLevel.Debug,
            BreadcrumbLevel.Info => Sentry.BreadcrumbLevel.Info,
            BreadcrumbLevel.Warning => Sentry.BreadcrumbLevel.Warning,
            BreadcrumbLevel.Error => Sentry.BreadcrumbLevel.Error,
            BreadcrumbLevel.Critical => Sentry.BreadcrumbLevel.Error,
            _ => Sentry.BreadcrumbLevel.Info
        };
    }
    #endregion
}