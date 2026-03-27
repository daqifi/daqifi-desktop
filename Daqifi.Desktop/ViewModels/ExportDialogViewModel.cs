using System.IO;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Exporter;
using Daqifi.Desktop.Logger;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Daqifi.Desktop.ViewModels;

public partial class ExportDialogViewModel : ObservableObject
{
    #region Private Variables
    private readonly List<int> _sessionsIds;
    private string _exportFilePath;
    private CancellationTokenSource _cts;

    [ObservableProperty]
    private bool _exportAllSelected = true;
    [ObservableProperty]
    private bool _exportAverageSelected;
    [ObservableProperty]
    private bool _isExporting;
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
    #endregion

    #region Private Methods

    [RelayCommand]
    private void BrowseExportPath()
    {
        var dialog = new SaveFileDialog
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

    [RelayCommand]
    private async Task ExportLoggingSessions()
    {
        if (string.IsNullOrWhiteSpace(ExportFilePath)) { return; }

        IsExporting = true;
        _cts = new CancellationTokenSource();
        var cancellationToken = _cts.Token;
        var progress = new Progress<int>(progressValue => ExportProgress = progressValue);

        try
        {
            await Task.Run(async () =>
            {
                var totalSessions = _sessionsIds.Count;
                for (var i = 0; i < totalSessions; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    var sessionId = _sessionsIds[i];
                    var loggingSession = await GetLoggingSessionFromId(sessionId);
                    var sessionName = LoggingManager.Instance.LoggingSessions.FirstOrDefault(s => s.ID == sessionId).Name;
                    var filepath = totalSessions > 1
                        ? Path.Combine(ExportFilePath, $"{sessionName}.csv")
                        : ExportFilePath;

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
        }
        catch (OperationCanceledException)
        {
            AppLogger.Instance.Information("Export operation was cancelled by user.");
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error(ex, "Problem Exporting Data");
        }
        finally
        {
            IsExporting = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private void ExportAllSamples(LoggingSession session, string filepath, IProgress<int> progress, CancellationToken cancellationToken, int sessionIndex, int totalSessions)
    {
        var loggingSessionExporter = new OptimizedLoggingSessionExporter();
        loggingSessionExporter.ExportLoggingSession(session, filepath, ExportRelativeTime, progress, cancellationToken, sessionIndex, totalSessions);
    }

    private void ExportAverageSamples(LoggingSession session, string filepath, IProgress<int> progress, CancellationToken cancellationToken, int sessionIndex, int totalSessions)
    {
        var loggingSessionExporter = new OptimizedLoggingSessionExporter();
        loggingSessionExporter.ExportAverageSamples(session, filepath, AverageQuantity, ExportRelativeTime, progress, cancellationToken, sessionIndex, totalSessions);
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
}
