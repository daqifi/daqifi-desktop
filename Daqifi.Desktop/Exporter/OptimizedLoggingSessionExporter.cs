using System.IO;
using System.Text;
using Daqifi.Core.Logging.Export;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Daqifi.Desktop.Exporter;

/// <summary>
/// Thin desktop adapter around <see cref="CsvExporter"/>: builds an
/// <see cref="ISampleSource"/> from a <see cref="LoggingSession"/>, maps
/// desktop options into <see cref="CsvExportOptions"/>, and folds per-session
/// progress into the multi-session percentage the dialog expects.
/// </summary>
public class OptimizedLoggingSessionExporter
{
    #region Constants
    private const int BUFFER_SIZE = 1024 * 1024; // 1MB buffer for file writes
    #endregion

    #region Private Fields
    private readonly AppLogger _appLogger = AppLogger.Instance;
    private readonly string _delimiter = DaqifiSettings.Instance.CsvDelimiter;
    private readonly IDbContextFactory<LoggingContext> _loggingContext;
    #endregion

    #region Constructors
    public OptimizedLoggingSessionExporter()
    {
        if (App.ServiceProvider != null)
        {
            _loggingContext = App.ServiceProvider.GetRequiredService<IDbContextFactory<LoggingContext>>();
        }
    }

    public OptimizedLoggingSessionExporter(IDbContextFactory<LoggingContext> loggingContext)
    {
        _loggingContext = loggingContext;
    }
    #endregion

    #region Public Methods
    public void ExportLoggingSession(LoggingSession loggingSession, string filepath, bool exportRelativeTime,
        IProgress<int> progress, CancellationToken cancellationToken, int sessionIndex, int totalSessions)
    {
        try
        {
            var source = TryBuildSource(loggingSession);
            if (source == null)
            {
                return;
            }

            var options = new CsvExportOptions
            {
                Delimiter = _delimiter,
                UseRelativeTime = exportRelativeTime,
            };

            RunExport(source, filepath, options, progress, cancellationToken, sessionIndex, totalSessions);
        }
        catch (OperationCanceledException)
        {
            // Let the viewmodel's cancellation handler record the breadcrumb.
            throw;
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Exception in OptimizedExportLoggingSession");
        }
    }

    public void ExportAverageSamples(LoggingSession session, string filepath, double averageQuantity,
        bool exportRelativeTime, IProgress<int> progress, CancellationToken cancellationToken, int sessionIndex, int totalSessions)
    {
        try
        {
            var window = (int)averageQuantity;
            if (window <= 0)
            {
                _appLogger.Warning($"Skipping average export: AverageWindow must be positive (was {averageQuantity}).");
                return;
            }

            var source = TryBuildSource(session);
            if (source == null)
            {
                return;
            }

            var options = new CsvExportOptions
            {
                Delimiter = _delimiter,
                UseRelativeTime = exportRelativeTime,
                AverageWindow = window,
            };

            RunExport(source, filepath, options, progress, cancellationToken, sessionIndex, totalSessions);
        }
        catch (OperationCanceledException)
        {
            // Let the viewmodel's cancellation handler record the breadcrumb.
            throw;
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Failed in OptimizedExportAverageSamples");
        }
    }
    #endregion

    #region Private Helpers
    /// <summary>
    /// Builds the appropriate <see cref="ISampleSource"/> for this session, or
    /// returns null if there is no usable data source. Mirrors the legacy
    /// "no file when there are no channels" behavior so empty sessions don't
    /// produce empty CSVs.
    /// </summary>
    private ISampleSource TryBuildSource(LoggingSession loggingSession)
    {
        ISampleSource source;
        if (loggingSession.DataSamples?.Any() == true)
        {
            source = new LoggingSessionSampleSource(loggingSession, loggingSession.DataSamples);
        }
        else if (_loggingContext != null)
        {
            source = new LoggingSessionSampleSource(loggingSession, _loggingContext);
        }
        else
        {
            _appLogger.Warning("No data source available for export");
            return null;
        }

        if (source.GetChannels().Count == 0)
        {
            return null;
        }

        return source;
    }

    private static void RunExport(ISampleSource source, string filepath, CsvExportOptions options,
        IProgress<int> progress, CancellationToken cancellationToken, int sessionIndex, int totalSessions)
    {
        // Fold the [0..100] per-session progress that core reports into the
        // overall (sessionIndex + p/100) * (100/totalSessions) shape the dialog
        // already binds to. Match the legacy ceiling so we never exceed 100.
        IProgress<int> wrappedProgress = null;
        if (progress != null && totalSessions > 0)
        {
            wrappedProgress = new Progress<int>(p =>
            {
                var bounded = Math.Min(100, Math.Max(0, p));
                var overall = (int)((sessionIndex + bounded / 100.0) * (100.0 / totalSessions));
                progress.Report(overall);
            });
        }

        using var writer = new StreamWriter(filepath, false, Encoding.UTF8, BUFFER_SIZE);
        var exporter = new CsvExporter();
        exporter.ExportAsync(source, writer, options, wrappedProgress, cancellationToken)
            .GetAwaiter()
            .GetResult();
    }
    #endregion
}
