using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Exporter;
using Daqifi.Desktop.Logger;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
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
    private BackgroundWorker bw;
    [RelayCommand]
    private void CancelExport()
    {
        if (bw != null && bw.WorkerSupportsCancellation)
        {
            bw.CancelAsync();
        }
    }
    
    [RelayCommand]
    private void ExportLoggingSessions()
    {
        IsExporting = true;
        if (string.IsNullOrWhiteSpace(ExportFilePath)) { return; }
        bw = new BackgroundWorker
        {
            WorkerReportsProgress = true,
            WorkerSupportsCancellation = true
        };
        bw.DoWork += async (sender, args) =>
        {
            var totalSessions = _sessionsIds.Count;
            for (var i = 0; i < totalSessions; i++)
            {
                if (bw.CancellationPending)
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
                    ExportAllSamples(loggingSession, filepath, bw, i, totalSessions);
                }
                else if (ExportAverageSelected)
                {
                    ExportAverageSamples(loggingSession, filepath, bw, i, totalSessions);
                }
            }
        };
        bw.ProgressChanged += UploadFirmwareProgressChanged;
        bw.RunWorkerCompleted += (sender, args) =>
        {
            if (args.Error != null)
            {
                AppLogger.Instance.Error(args.Error, "Problem Exporting Data");
            }
            else
            {
                IsExporting = false;
            }
        };
        bw.RunWorkerAsync();
    }

    private void UploadFirmwareProgressChanged(object? sender, ProgressChangedEventArgs e)
    {
        ExportProgress = e.ProgressPercentage;
    }

    private void ExportAllSamples(LoggingSession session, string filepath, BackgroundWorker bw, int sessionIndex, int totalSessions)
    {
        var loggingSessionExporter = new LoggingSessionExporter();
        loggingSessionExporter.ExportLoggingSession(session, filepath, ExportRelativeTime, bw, sessionIndex, totalSessions);
    }

    private void ExportAverageSamples(LoggingSession session, string filepath, BackgroundWorker bw, int sessionIndex, int totalSessions)
    {
        var loggingSessionExporter = new LoggingSessionExporter();
        loggingSessionExporter.ExportAverageSamples(session, filepath, AverageQuantity, ExportRelativeTime, bw, sessionIndex, totalSessions);
    }

    private async Task<LoggingSession> GetLoggingSessionFromId(int sessionId)
    {
        using (var context = _loggingContext.CreateDbContext())
        {
            var loggingSession = await context.Sessions
                .AsNoTracking()
                .Where(s => s.ID == sessionId)
                .Select(s => new LoggingSession
                {
                    ID = s.ID,
                    DataSamples = s.DataSamples.Select(d => new DataSample
                    {
                        ID = d.ID,
                        Value = d.Value,
                        TimestampTicks = d.TimestampTicks,
                        ChannelName = d.ChannelName,
                        DeviceSerialNo = d.DeviceSerialNo,
                        DeviceName = d.DeviceName
                    }).ToList()
                }).FirstOrDefaultAsync();
            return loggingSession;
        }
    }
    #endregion
}