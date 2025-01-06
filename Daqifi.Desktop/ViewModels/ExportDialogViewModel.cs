using Daqifi.Desktop.Commands;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Exporter;
using Daqifi.Desktop.Logger;
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
        private int _averageQuantity = 2;
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

        public bool _exportRelativeTime;
        public bool ExportRelativeTime
        {
            get => _exportRelativeTime;
            set
            {
                _exportRelativeTime = value;
                OnPropertyChanged();
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

        public bool ExportAverageSelected
        {
            get => _exportAverageSelected;
            set
            {
                _exportAverageSelected = value;
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
        #endregion

        #region Constructor

        private readonly IDbContextFactory<LoggingContext> _loggingContext;
        public ExportDialogViewModel(int sessionId)
        {
            _loggingContext = App.ServiceProvider.GetRequiredService<IDbContextFactory<LoggingContext>>();
            _sessionsIds = new List<int>() { sessionId };
            ExportSessionCommand = new DelegateCommand(ExportLoggingSessions, CanExportSession);
            BrowseExportPathCommand = new DelegateCommand(BrowseExportPath, CanBrowseExportPath);
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

        private void ExportLoggingSessions(object o)
        {
            if (string.IsNullOrWhiteSpace(ExportFilePath)) { return; }

            var bw = new BackgroundWorker();
            bw.DoWork += delegate
            {
                foreach (var sessionId in _sessionsIds)
                {
                    var loggingSession = GetLoggingSessionFromId(sessionId);

                    var filepath = _sessionsIds.Count > 1 ? Path.Combine(ExportFilePath, $"{loggingSession.Name}.csv") : ExportFilePath;

                    if (ExportAllSelected)
                    {
                        ExportAllSamples(loggingSession, filepath);
                    }
                    else if (ExportAverageSelected)
                    {
                        ExportAverageSamples(loggingSession, filepath);
                    }
                }
            };

            bw.RunWorkerAsync();
        }

        private void ExportAllSamples(LoggingSession session, string filepath)
        {
            var loggingSessionExporter = new LoggingSessionExporter();
            loggingSessionExporter.ExportLoggingSession(session, filepath, ExportRelativeTime);
        }

        private void ExportAverageSamples(LoggingSession session, string filepath)
        {
            var loggingSessionExporter = new LoggingSessionExporter();
            loggingSessionExporter.ExportAverageSamples(session, filepath, AverageQuantity, ExportRelativeTime);
        }

        private LoggingSession GetLoggingSessionFromId(int sessionId)
        {
            using (var context = _loggingContext.CreateDbContext())
            {
                context.ChangeTracker.AutoDetectChangesEnabled = false;

                var loggingSession = context.Sessions
                   .Include(s => s.DataSamples)
                   .FirstOrDefault(s => s.ID == sessionId);
                return loggingSession;
            }
        }
        #endregion
    }
}