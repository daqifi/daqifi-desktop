using System;
using Daqifi.Desktop.Common.Loggers;
using Microsoft.Extensions.Logging;

namespace Daqifi.Desktop.Loggers;

/// <summary>
/// Bridges <see cref="Microsoft.Extensions.Logging"/> (used by Daqifi.Core — notably the firmware
/// update service) into the desktop's NLog-backed <see cref="AppLogger"/>, so Core's diagnostics —
/// including the WiFi flash tool output, progress, and state transitions — land in
/// <c>DAQifiAppLog.log</c> alongside the desktop's own logs. Information level and above is
/// forwarded; Debug/Trace are dropped to avoid flooding the file.
/// </summary>
public sealed class AppLoggerLoggerProvider : ILoggerProvider
{
    private readonly IAppLogger _appLogger;

    /// <summary>
    /// Initializes the provider.
    /// </summary>
    /// <param name="appLogger">
    /// Target desktop logger. Defaults to <see cref="AppLogger.Instance"/> when null.
    /// </param>
    public AppLoggerLoggerProvider(IAppLogger? appLogger = null)
    {
        _appLogger = appLogger ?? AppLogger.Instance;
    }

    /// <summary>
    /// Creates an <see cref="ILogger"/> that forwards entries for the given category to the
    /// desktop <see cref="AppLogger"/>.
    /// </summary>
    /// <param name="categoryName">The logger category (typically a fully-qualified type name).</param>
    public ILogger CreateLogger(string categoryName) => new AppLoggerBridge(_appLogger, categoryName);

    /// <summary>No-op: the provider holds no disposable resources (the target logger is shared).</summary>
    public void Dispose()
    {
    }

    private sealed class AppLoggerBridge : ILogger
    {
        private readonly IAppLogger _appLogger;
        private readonly string _category;
        private readonly bool _isDaqifiCategory;

        public AppLoggerBridge(IAppLogger appLogger, string category)
        {
            _appLogger = appLogger;

            // Categories are normally non-null, but guard anyway (the App.xaml.cs filter also
            // anticipates null) so logger creation can never throw on a bad category.
            var normalizedCategory = string.IsNullOrWhiteSpace(category) ? "Unknown" : category;

            // Our own (DAQiFi/Core) errors are worth capturing to Sentry; framework errors are not.
            _isDaqifiCategory = normalizedCategory.StartsWith("Daqifi", StringComparison.OrdinalIgnoreCase);

            // Tag log lines with the full category for unambiguous attribution. (A short leaf name
            // would read nicer but collides across namespaces — e.g. two ".Foo" types — which
            // matters now that framework Error+ logs are forwarded here too.)
            _category = normalizedCategory;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        // Forward Information+ (Debug/Trace are dropped to avoid flooding the file); exclude the
        // LogLevel.None sentinel, whose integer value (6) is above Information.
        public bool IsEnabled(LogLevel logLevel) => logLevel is >= LogLevel.Information and < LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel) || formatter is null)
            {
                return;
            }

            // Logging is best-effort: a failure here (formatter or sink) must never crash the
            // operation being logged, so guard the whole forwarding path.
            try
            {
                var message = formatter(state, exception);
                if (string.IsNullOrEmpty(message) && exception is null)
                {
                    return;
                }

                var line = $"[{_category}] {message}";
                switch (logLevel)
                {
                    case LogLevel.Critical:
                    case LogLevel.Error:
                        // Capture our own errors to Sentry (AppLogger.Error); route framework
                        // errors to the file only (AppLogger.Warning does NOT hit Sentry) so routine
                        // component Error logs don't spam Sentry. Tag the level so severity isn't lost.
                        if (_isDaqifiCategory)
                        {
                            if (exception is not null)
                            {
                                _appLogger.Error(exception, line);
                            }
                            else
                            {
                                _appLogger.Error(line);
                            }
                        }
                        else
                        {
                            var framework = $"[{logLevel}] {line}";
                            if (exception is not null)
                            {
                                _appLogger.Warning(exception, framework);
                            }
                            else
                            {
                                _appLogger.Warning(framework);
                            }
                        }

                        break;
                    case LogLevel.Warning:
                        if (exception is not null)
                        {
                            _appLogger.Warning(exception, line);
                        }
                        else
                        {
                            _appLogger.Warning(line);
                        }

                        break;
                    default:
                        // IAppLogger has no Information(Exception, ...) overload, so append the
                        // exception text to preserve the stack trace if Core ever logs one at
                        // Information (e.g. a diagnostic/retry path) rather than dropping it.
                        _appLogger.Information(exception is null ? line : $"{line}{Environment.NewLine}{exception}");
                        break;
                }
            }
            catch (Exception bridgeFailure)
            {
                // The bridge must never propagate into the caller, but don't fail silently either:
                // make a best-effort note of the failure, guarding the last-resort write too since
                // AppLogger itself may be what failed.
                try
                {
                    _appLogger.Warning(
                        bridgeFailure,
                        $"[{_category}] AppLogger logging bridge failed to forward an entry.");
                }
                catch
                {
                    // Nothing safe left to do — the logging sink itself is unavailable.
                }
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
