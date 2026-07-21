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

    #region Static State
    // CsvExporter is stateless and ships from a sibling library that isn't registered with our DI
    // container. Caching one instance avoids re-allocating per export without forcing every caller
    // to inject it.
    private static readonly CsvExporter SharedCsvExporter = new();
    #endregion

    #region Private Fields
    private readonly AppLogger _appLogger = AppLogger.Instance;
    private readonly string _delimiter = DaqifiSettings.Instance.CsvDelimiter;
    private readonly IDbContextFactory<LoggingContext> _loggingContext;
    #endregion

    #region Constructors
    /// <summary>
    /// Creates an exporter that resolves its <see cref="LoggingContext"/> factory from
    /// <see cref="App.ServiceProvider"/>. Used in production where DI is wired up.
    /// </summary>
    public OptimizedLoggingSessionExporter()
    {
        if (App.ServiceProvider != null)
        {
            _loggingContext = App.ServiceProvider.GetRequiredService<IDbContextFactory<LoggingContext>>();
        }
    }

    /// <summary>
    /// Creates an exporter with an explicit <see cref="LoggingContext"/> factory. Used in tests
    /// that spin up a temp SQLite database.
    /// </summary>
    public OptimizedLoggingSessionExporter(IDbContextFactory<LoggingContext> loggingContext)
    {
        _loggingContext = loggingContext;
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// Exports every sample in <paramref name="loggingSession"/> as one CSV row per timestamp.
    /// </summary>
    /// <param name="loggingSession">Session to export. May carry an in-memory <c>DataSamples</c>
    /// collection (test path) or just an <c>ID</c> the EF context will look up (production path).</param>
    /// <param name="filepath">Absolute path to the output CSV file.</param>
    /// <param name="exportRelativeTime">When true, the time column is seconds-since-first-sample; otherwise ISO 8601.</param>
    /// <param name="progress">Optional progress sink reporting overall percentage across all sessions.</param>
    /// <param name="cancellationToken">Token that aborts the export.</param>
    /// <param name="sessionIndex">Zero-based index of this session within a multi-session export.</param>
    /// <param name="totalSessions">Total number of sessions in this export run.</param>
    /// <exception cref="IOException">The destination file could not be written — most commonly
    /// because it is open in another program (issue #747).</exception>
    /// <remarks>Failures propagate to the caller: <see cref="ViewModels.ExportDialogViewModel"/> is the
    /// single place that classifies them, logs them once, and tells the user. Swallowing them here
    /// used to leave the dialog reporting "Export complete" over a file that was never written.</remarks>
    public void ExportLoggingSession(LoggingSession loggingSession, string filepath, bool exportRelativeTime,
        IProgress<int> progress, CancellationToken cancellationToken, int sessionIndex, int totalSessions)
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

    /// <summary>
    /// Exports samples grouped into rolling windows of <paramref name="averageQuantity"/> samples,
    /// writing one averaged row per window. Trailing partial windows are flushed (a behavior change
    /// vs the legacy implementation, which silently dropped them).
    /// </summary>
    /// <param name="session">Session to export.</param>
    /// <param name="filepath">Absolute path to the output CSV file.</param>
    /// <param name="averageQuantity">Window size in samples. Must be positive; non-positive values short-circuit with a warning log.</param>
    /// <param name="exportRelativeTime">When true, the time column is seconds-since-first-sample; otherwise ISO 8601.</param>
    /// <param name="progress">Optional progress sink reporting overall percentage across all sessions.</param>
    /// <param name="cancellationToken">Token that aborts the export.</param>
    /// <param name="sessionIndex">Zero-based index of this session within a multi-session export.</param>
    /// <param name="totalSessions">Total number of sessions in this export run.</param>
    /// <exception cref="IOException">The destination file could not be written — most commonly
    /// because it is open in another program (issue #747).</exception>
    /// <remarks>Failures propagate to the caller; see <see cref="ExportLoggingSession"/>.</remarks>
    public void ExportAverageSamples(LoggingSession session, string filepath, double averageQuantity,
        bool exportRelativeTime, IProgress<int> progress, CancellationToken cancellationToken, int sessionIndex, int totalSessions)
    {
        var window = (int)averageQuantity;
        if (window <= 0)
        {
            _appLogger.Warning(
                $"Skipping average export: AverageWindow must be positive (was {averageQuantity}, sessionId={session?.ID}, filepath={filepath}).");
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
        SharedCsvExporter.ExportAsync(source, writer, options, wrappedProgress, cancellationToken)
            .GetAwaiter()
            .GetResult();
    }
    #endregion
}
