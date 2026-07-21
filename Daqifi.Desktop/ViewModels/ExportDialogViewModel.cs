using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Exporter;
using Daqifi.Desktop.Logger;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Daqifi.Desktop.ViewModels;

public partial class ExportDialogViewModel : ObservableObject
{
    #region Private Variables
    private readonly List<int> _sessionsIds;
    private string _exportFilePath;
    private CancellationTokenSource _cts;

    // When true, every session is written as a separate "{session}.csv" file inside the
    // directory named by <see cref="ExportFilePath"/>, regardless of how many sessions are
    // being exported. Set only by the UI-test export hook (<see cref="ExportToDirectoryAsync"/>)
    // so a single-session export through the hook still lands in a directory the harness owns;
    // the interactive dialog leaves this false and keeps the file/directory-per-count behaviour.
    private bool _forceDirectoryLayout;

    [ObservableProperty]
    private bool _exportAllSelected = true;
    [ObservableProperty]
    private bool _exportAverageSelected;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfiguring))]
    private bool _isExporting;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfiguring))]
    private bool _isExportComplete;
    [ObservableProperty]
    private bool _exportSucceeded;
    [ObservableProperty]
    private string _exportResultMessage;
    [ObservableProperty]
    private int _averageQuantity = 2;
    [ObservableProperty]
    private string _exportProgressText;
    [ObservableProperty]
    private bool _exportRelativeTime;
    private int _exportProgress;
    #endregion

    #region Properties
    public string ExportFilePath
    {
        get => _exportFilePath;
        set
        {
            _exportFilePath = value;
            OnPropertyChanged();
            ExportLoggingSessionsCommand.NotifyCanExecuteChanged();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public int ExportProgress
    {
        get => _exportProgress;
        set
        {
            _exportProgress = value;
            OnPropertyChanged();
            ExportProgressText = $"Exporting progress: {ExportProgress}% completed";
        }
    }

    /// <summary>
    /// True while the dialog is showing its configuration form — i.e. not mid-export and not
    /// showing the completion result. Selects which of the dialog's three states is visible.
    /// </summary>
    public bool IsConfiguring => !IsExporting && !IsExportComplete;
    #endregion

    #region Commands
    public ICommand BrowseCommand { get; private set; }
    #endregion

    #region Constructor

    private readonly IDbContextFactory<LoggingContext> _loggingContext;
    public ExportDialogViewModel(int sessionId)
    {
        _loggingContext = App.ServiceProvider.GetRequiredService<IDbContextFactory<LoggingContext>>();
        _sessionsIds = [sessionId];
        BrowseCommand = BrowseExportPathCommand;
    }

    public ExportDialogViewModel(IEnumerable<LoggingSession> sessions)
    {
        _loggingContext = App.ServiceProvider.GetRequiredService<IDbContextFactory<LoggingContext>>();
        _sessionsIds = sessions.Select(s => s.ID).ToList();
        BrowseCommand = BrowseExportDirectoryCommand;
    }

    /// <summary>
    /// Test seam: supplies the logging-context factory directly so unit tests can exercise the
    /// dialog's state machine and export flow without booting the App/DI container.
    /// </summary>
    internal ExportDialogViewModel(IDbContextFactory<LoggingContext> loggingContext, int sessionId)
    {
        _loggingContext = loggingContext;
        _sessionsIds = [sessionId];
        BrowseCommand = BrowseExportPathCommand;
    }
    #endregion

    #region Private Methods

    [RelayCommand]
    private void BrowseExportPath()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            DefaultExt = ".csv",
            Filter = "Log File|*.csv"
        };

        var result = dialog.ShowDialog();

        if (result == false) { return; }

        ExportFilePath = dialog.FileName;
    }

    [RelayCommand]
    private void BrowseExportDirectory()
    {
        var dialog = new FolderBrowserDialog();

        var result = dialog.ShowDialog();

        if (result == DialogResult.Cancel) { return; }

        ExportFilePath = dialog.SelectedPath;
    }

    [RelayCommand]
    private void CancelExport()
    {
        _cts?.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportLoggingSessions()
    {
        if (string.IsNullOrWhiteSpace(ExportFilePath)) { return; }

        IsExporting = true;
        IsExportComplete = false;
        _cts = new CancellationTokenSource();
        var cancellationToken = _cts.Token;
        var progress = new Progress<int>(progressValue => ExportProgress = progressValue);
        AppLogger.Instance.AddBreadcrumb("export", $"Data export started ({_sessionsIds.Count} session(s))");

        var cancelled = false;
        var failed = false;

        // Non-null once we can tell the user *why* the export failed (a locked or unwritable
        // destination). Everything else falls back to the generic message.
        string failureReason = null;

        // The destination currently being written, so the catch below can name the offending
        // file even though the exception surfaces from deep inside the exporter.
        var currentFilepath = ExportFilePath;
        try
        {
            var targets = ResolveExportTargets();

            // Pre-flight: the overwhelmingly common failure is a destination CSV still open in
            // Excel/Spyder (issue #747). Checking every target before writing anything means the
            // user gets an actionable message immediately instead of a partial export. This is
            // inherently racy, so the catch below still handles a lock taken after the check.
            foreach (var target in targets)
            {
                failureReason = DescribeUnwritableDestination(target.Filepath);
                if (failureReason == null) { continue; }

                currentFilepath = target.Filepath;
                break;
            }

            if (failureReason != null)
            {
                failed = true;
                AppLogger.Instance.Warning($"Export aborted: {failureReason}");
                AppLogger.Instance.AddBreadcrumb("export", "Data export blocked by destination file",
                    Common.Loggers.BreadcrumbLevel.Warning);
            }
            else
            {
                await Task.Run(async () =>
                {
                    var totalSessions = targets.Count;
                    for (var i = 0; i < totalSessions; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var sessionId = targets[i].SessionId;
                        var loggingSession = await GetLoggingSessionFromId(sessionId);
                        if (loggingSession == null)
                        {
                            AppLogger.Instance.Warning(
                                $"Skipping export for session {sessionId}: it was not found in the database.");
                            continue;
                        }

                        currentFilepath = targets[i].Filepath;

                        if (ExportAllSelected)
                        {
                            ExportAllSamples(loggingSession, currentFilepath, progress, cancellationToken, i, totalSessions);
                        }
                        else if (ExportAverageSelected)
                        {
                            ExportAverageSamples(loggingSession, currentFilepath, progress, cancellationToken, i, totalSessions);
                        }
                    }
                }, cancellationToken);

                // Reaching here means the loop ran to completion without a cancellation being
                // observed at a checkpoint — a Cancel click that lands after the work is done no
                // longer suppresses the result state. Cancellation is signalled only by the
                // OperationCanceledException path below.
                AppLogger.Instance.AddBreadcrumb("export", "Data export completed");
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            AppLogger.Instance.Information("Export operation was cancelled by user.");
            AppLogger.Instance.AddBreadcrumb("export", "Data export cancelled", Common.Loggers.BreadcrumbLevel.Warning);
        }
        catch (Exception ex) when (IsDestinationBlocked(ex))
        {
            // A destination that cannot be written — locked by another program, read-only, denied,
            // or on a folder that disappeared — is a user/environmental condition, not an app bug.
            // Log it as a warning (stack trace stays in the local log, no Sentry event) the same way
            // the device layer classifies an unreachable device (issues #517 / #740). Anything else,
            // including a database-level I/O error, still takes the Error/Sentry path below.
            failed = true;
            failureReason = DescribeFileFailure(ex, currentFilepath);
            AppLogger.Instance.Warning(ex, $"Export failed writing '{currentFilepath}'.");
            AppLogger.Instance.AddBreadcrumb("export", "Data export blocked by destination file",
                Common.Loggers.BreadcrumbLevel.Warning);
        }
        catch (Exception ex)
        {
            failed = true;
            AppLogger.Instance.Error(ex, "Problem Exporting Data");
            AppLogger.Instance.AddBreadcrumb("export", "Data export failed", Common.Loggers.BreadcrumbLevel.Error);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;

            // A cancel returns to the configuration form; a success or failure shows the
            // result state (set complete before clearing IsExporting so IsConfiguring never
            // flips true in between).
            if (!cancelled)
            {
                ExportSucceeded = !failed;
                ExportResultMessage = failed
                    ? failureReason ?? "Export failed. Please try again."
                    : "Export complete";
                IsExportComplete = true;
            }

            IsExporting = false;
        }
    }

    /// <summary>
    /// Returns the dialog to its configuration form after a failure so the user can close the
    /// program holding the file (or pick a different destination) and export again.
    /// </summary>
    [RelayCommand]
    private void RetryExport()
    {
        IsExportComplete = false;
        ExportProgress = 0;
        ExportResultMessage = null;
    }

    private bool CanExport => !string.IsNullOrWhiteSpace(ExportFilePath);

    /// <summary>
    /// Opens the export destination in File Explorer: the folder itself for a directory export,
    /// or the containing folder with the file selected for a single-file export.
    /// </summary>
    [RelayCommand]
    private void OpenExportLocation()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ExportFilePath)) { return; }

            if (Directory.Exists(ExportFilePath))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{ExportFilePath}\"") { UseShellExecute = true });
            }
            else if (File.Exists(ExportFilePath))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{ExportFilePath}\"") { UseShellExecute = true });
            }
            else
            {
                var directory = Path.GetDirectoryName(ExportFilePath);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Warning(ex, $"Could not open export location '{ExportFilePath}'.");
        }
    }

    private void ExportAllSamples(LoggingSession session, string filepath, IProgress<int> progress, CancellationToken cancellationToken, int sessionIndex, int totalSessions)
    {
        var loggingSessionExporter = new OptimizedLoggingSessionExporter(_loggingContext);
        loggingSessionExporter.ExportLoggingSession(session, filepath, ExportRelativeTime, progress, cancellationToken, sessionIndex, totalSessions);
    }

    private void ExportAverageSamples(LoggingSession session, string filepath, IProgress<int> progress, CancellationToken cancellationToken, int sessionIndex, int totalSessions)
    {
        var loggingSessionExporter = new OptimizedLoggingSessionExporter(_loggingContext);
        loggingSessionExporter.ExportAverageSamples(session, filepath, AverageQuantity, ExportRelativeTime, progress, cancellationToken, sessionIndex, totalSessions);
    }

    /// <summary>
    /// One session mapped to the file it will be written to.
    /// </summary>
    private sealed record ExportTarget(int SessionId, string Filepath);

    /// <summary>
    /// Resolves the destination file for every selected session up front, so the export can be
    /// pre-flighted before any data is written. Per-session file naming only applies when writing
    /// into a directory (multi-session export, or the directory-layout test hook); a plain
    /// single-file export writes straight to <see cref="ExportFilePath"/> and so doesn't depend on
    /// LoggingManager. Sessions no longer in the in-memory list fall back to "Session_{id}".
    /// </summary>
    private List<ExportTarget> ResolveExportTargets()
    {
        var targets = new List<ExportTarget>(_sessionsIds.Count);
        var perSessionFiles = _forceDirectoryLayout || _sessionsIds.Count > 1;

        foreach (var sessionId in _sessionsIds)
        {
            string filepath;
            if (perSessionFiles)
            {
                var sessionName = LoggingManager.Instance.LoggingSessions
                    .FirstOrDefault(s => s.ID == sessionId)?.Name ?? $"Session_{sessionId}";
                filepath = Path.Combine(ExportFilePath, $"{MakeSafeFileName(sessionName)}.csv");
            }
            else
            {
                filepath = ExportFilePath;
            }

            targets.Add(new ExportTarget(sessionId, filepath));
        }

        return targets;
    }

    /// <summary>
    /// Returns a user-facing reason why <paramref name="filepath"/> cannot be written, or null when
    /// it looks writable. Only an existing file is probed — opening it for writing with no sharing
    /// fails exactly when another program holds it — and the probe never truncates or creates
    /// anything, so a blocked export leaves the destination untouched.
    /// </summary>
    private static string DescribeUnwritableDestination(string filepath)
    {
        try
        {
            if (!File.Exists(filepath)) { return null; }

            using var probe = new FileStream(filepath, FileMode.Open, FileAccess.Write, FileShare.None);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DescribeFileFailure(ex, filepath);
        }
    }

    /// <summary>
    /// True when an export failure is the destination's fault — denied, gone, or held by another
    /// program. Deliberately narrow: a generic <see cref="IOException"/> (a full disk, a failing
    /// drive, an EF/SQLite read error) keeps the default Error/Sentry treatment so real problems
    /// stay visible.
    /// </summary>
    private static bool IsDestinationBlocked(Exception ex) => ex switch
    {
        UnauthorizedAccessException => true,
        DirectoryNotFoundException => true,
        IOException io => IsSharingViolation(io),
        _ => false,
    };

    /// <summary>
    /// Turns a file-access failure into a message that tells the user what to do about it.
    /// </summary>
    private static string DescribeFileFailure(Exception ex, string filepath)
    {
        var name = string.IsNullOrWhiteSpace(filepath) ? "the export file" : $"'{Path.GetFileName(filepath)}'";

        return ex switch
        {
            UnauthorizedAccessException =>
                $"Could not write {name} — access was denied. Choose a different folder, or check that the file is not read-only.",
            DirectoryNotFoundException =>
                $"Could not write {name} — that folder no longer exists. Choose a different location and try again.",
            IOException io when IsSharingViolation(io) =>
                $"Could not write {name} — it is open in another program. Close it and try again.",
            _ =>
                $"Could not write {name}. {ex.Message}",
        };
    }

    /// <summary>
    /// True when the OS refused the handle because someone else already holds the file
    /// (ERROR_SHARING_VIOLATION / ERROR_LOCK_VIOLATION) — the "it's still open in Excel" case,
    /// as opposed to a full disk or an I/O error, which deserve a different message.
    /// </summary>
    private static bool IsSharingViolation(IOException ex)
    {
        const int FACILITY_WIN32 = unchecked((int)0x80070000);
        const int ERROR_SHARING_VIOLATION = 32;
        const int ERROR_LOCK_VIOLATION = 33;

        if ((ex.HResult & unchecked((int)0xFFFF0000)) != FACILITY_WIN32) { return false; }

        var win32Error = ex.HResult & 0xFFFF;
        return win32Error is ERROR_SHARING_VIOLATION or ERROR_LOCK_VIOLATION;
    }

    /// <summary>
    /// Replaces characters that are invalid in a file name (a session can be renamed to arbitrary
    /// text) with '_', so per-session export to <c>{name}.csv</c> never throws on a bad path.
    /// </summary>
    private static string MakeSafeFileName(string name)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name;
    }

    private async Task<LoggingSession> GetLoggingSessionFromId(int sessionId)
    {
        await using var context = _loggingContext.CreateDbContext();
        var loggingSession = await context.Sessions
            .AsNoTracking()
            .Where(s => s.ID == sessionId)
            .Select(s => new LoggingSession
            {
                ID = s.ID
            }).FirstOrDefaultAsync();
        return loggingSession;
    }
    #endregion

    #region Test Hook
    /// <summary>
    /// UI-test entry point: exports the configured session(s) straight into
    /// <paramref name="directory"/> — one <c>{session}.csv</c> per session — using the same
    /// exporter and the dialog's default options (all samples, absolute time), with no
    /// <c>SaveFileDialog</c>/<c>FolderBrowserDialog</c>. Wired only through the
    /// <see cref="Daqifi.Desktop.Common.AppDataPaths.TestExportPath"/> hook, so production
    /// export behaviour is unchanged. Awaitable so the harness can know when the file is on disk.
    /// </summary>
    /// <param name="directory">Destination directory (created if missing) the harness owns.</param>
    internal Task ExportToDirectoryAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        _forceDirectoryLayout = true;
        ExportFilePath = directory;
        return ExportLoggingSessionsCommand.ExecuteAsync(null);
    }
    #endregion
}
