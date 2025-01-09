using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Commands;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.Exporter;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.View;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;

namespace Daqifi.Desktop.ViewModels
{
    public class ExportDialogViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        #region Private Variables
        private readonly List<int> _sessionsIds;
        private string _exportFilePath;
        private bool _exportAllSelected = true;
        private bool _exportAverageSelected;
        private bool _isExporting;
        private int _averageQuantity = 2;
        private int _exportProgress;
        private readonly IDialogService _dialogService;
        #endregion

        #region Properties
        public AppLogger AppLogger = AppLogger.Instance;
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

        public bool ExportAllSelected
        {
            get => _exportAllSelected;
            set
            {
                _exportAllSelected = value;
                OnPropertyChanged();
            }
        }

        public int ExportProgress
        {
            get => _exportProgress;
            set
            {
                _exportProgress = value;
                OnPropertyChanged();
                OnPropertyChanged("ExportProgressText");
            }
        }
        public string ExportProgressText => ($"Exporting progress: {ExportProgress}% completed");

        public bool ExportAverageSelected
        {
            get => _exportAverageSelected;
            set
            {
                _exportAverageSelected = value;
                OnPropertyChanged();
            }
        }
        public bool IsExporting
        {
            get => _isExporting;
            set
            {
                _isExporting = value;
                OnPropertyChanged();
            }
        }

        public int AverageQuantity
        {
            get => _averageQuantity;
            set
            {
                _averageQuantity = value;
                OnPropertyChanged();
            }

        }
        #endregion

        #region Command Properties
        public ICommand BrowseExportPathCommand { get; }
        private bool CanBrowseExportPath(object o)
        {
            return true;
        }

        public ICommand ExportSessionCommand { get; }
        private bool CanExportSession(object o)
        {
            return true;
        }


        public ICommand CancelExportCommand { get; set; }
        private bool CanCancelExportCommand(object o)
        {
            return true;
        }
        #endregion

        #region Constructor

        private readonly IDbContextFactory<LoggingContext> _loggingContext;
        public ExportDialogViewModel(int sessionId)
        {
            _loggingContext = App.ServiceProvider.GetRequiredService<IDbContextFactory<LoggingContext>>();
            _sessionsIds = new List<int>() { sessionId };
            ExportSessionCommand = new DelegateCommand(ExportLoggingSessions, CanExportSession);
            BrowseExportPathCommand = new DelegateCommand(BrowseExportPath, CanBrowseExportPath);
            CancelExportCommand = new DelegateCommand(CancelExport, CanCancelExportCommand);

        }

        public ExportDialogViewModel(IEnumerable<LoggingSession> sessions)
        {
            _sessionsIds = sessions.Select(s => s.ID).ToList();
            ExportSessionCommand = new DelegateCommand(ExportLoggingSessions, CanExportSession);
            BrowseExportPathCommand = new DelegateCommand(BrowseExportDirectory, CanBrowseExportPath);
        }
        #endregion

        #region Private Methods

        private void BrowseExportPath(object o)
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

        private void BrowseExportDirectory(object o)
        {
            var dialog = new FolderBrowserDialog();

            var result = dialog.ShowDialog();

            if (result == DialogResult.Cancel) { return; }

            ExportFilePath = dialog.SelectedPath;
        }
        private BackgroundWorker bw;
        private void CancelExport(object o)
        {
            if (bw != null && bw.WorkerSupportsCancellation)
            {
                bw.CancelAsync();
            }
        }
        private void ExportLoggingSessions(object o)
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
                int totalSessions = _sessionsIds.Count;
                for (int i = 0; i < totalSessions; i++)
                {
                    if (bw.CancellationPending)
                        return;
                    var sessionId = _sessionsIds[i];
                    var loggingSession = await GetLoggingSessionFromId(sessionId);
                    var filepath = totalSessions > 1
                        ? Path.Combine(ExportFilePath, $"{loggingSession.Name}.csv")
                        : ExportFilePath;

                    if (ExportAllSelected)
                    {
                        ExportAllSamples(loggingSession, filepath, bw, i, totalSessions);
                    }
                    else if (ExportAverageSelected)
                    {
                        ExportAverageSamples(loggingSession, filepath);
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
            loggingSessionExporter.ExportLoggingSession(session, filepath, bw, sessionIndex, totalSessions);
        }

        private void ExportAverageSamples(LoggingSession session, string filepath)
        {
            var loggingSessionExporter = new LoggingSessionExporter();
            loggingSessionExporter.ExportAverageSamples(session, filepath, AverageQuantity);
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
}