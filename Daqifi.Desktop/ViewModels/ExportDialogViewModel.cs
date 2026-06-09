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
        try
        {
            await Task.Run(async () =>
            {
                var totalSessions = _sessionsIds.Count;
                for (var i = 0; i < totalSessions; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var sessionId = _sessionsIds[i];
                    var loggingSession = await GetLoggingSessionFromId(sessionId);
                    if (loggingSession == null)
                    {
                        AppLogger.Instance.Warning(
                            $"Skipping export for session {sessionId}: it was not found in the database.");
                        continue;
                    }

                    // Per-session file naming only applies when writing into a directory (multi-session
                    // export, or the directory-layout test hook). Resolve the display name lazily so a
                    // plain single-file export doesn't depend on LoggingManager — falling back to the
                    // default "Session_{id}" form when the session is no longer in the in-memory list.
                    string filepath;
                    if (_forceDirectoryLayout || totalSessions > 1)
                    {
                        var sessionName = LoggingManager.Instance.LoggingSessions
                            .FirstOrDefault(s => s.ID == sessionId)?.Name ?? $"Session_{sessionId}";
                        filepath = Path.Combine(ExportFilePath, $"{MakeSafeFileName(sessionName)}.csv");
                    }
                    else
                    {
                        filepath = ExportFilePath;
                    }

                    if (ExportAllSelected)
                    {
                        ExportAllSamples(loggingSession, filepath, progress, cancellationToken, i, totalSessions);
                    }
                    else if (ExportAverageSelected)
                    {
                        ExportAverageSamples(loggingSession, filepath, progress, cancellationToken, i, totalSessions);
                    }
                }
            }, cancellationToken);

            // Reaching here means the loop ran to completion without a cancellation being
            // observed at a checkpoint — a Cancel click that lands after the work is done no
            // longer suppresses the result state. Cancellation is signalled only by the
            // OperationCanceledException path below.
            AppLogger.Instance.AddBreadcrumb("export", "Data export completed");
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            AppLogger.Instance.Information("Export operation was cancelled by user.");
            AppLogger.Instance.AddBreadcrumb("export", "Data export cancelled", Common.Loggers.BreadcrumbLevel.Warning);
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
                ExportResultMessage = failed ? "Export failed. Please try again." : "Export complete";
                IsExportComplete = true;
            }

            IsExporting = false;
        }
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
