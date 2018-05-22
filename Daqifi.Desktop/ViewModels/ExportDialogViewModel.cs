using Daqifi.Desktop.Commands;
using Daqifi.Desktop.Exporter;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.Loggers;
using GalaSoft.MvvmLight;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Windows.Input;

namespace Daqifi.Desktop.ViewModels
{
    public class ExportDialogViewModel : ViewModelBase
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
                RaisePropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool ExportAllSelected
        {
            get => _exportAllSelected;
            set
            {
                _exportAllSelected = value;
                RaisePropertyChanged();
            }
        }

        public bool ExportAverageSelected
        {
            get => _exportAverageSelected;
            set
            {
                _exportAverageSelected = value;
                RaisePropertyChanged();
            }
        }

        public int AverageQuantity
        {
            get => _averageQuantity;
            set
            {
                _averageQuantity = value;
                RaisePropertyChanged();
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
        public ExportDialogViewModel(int sessionId)
        {
            _sessionsIds = new List<int>() {sessionId};
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

            if (result == false) return;

            ExportFilePath = dialog.FileName;
        }

        private void BrowseExportDirectory(object o)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();

            var result = dialog.ShowDialog();

            if (result == System.Windows.Forms.DialogResult.Cancel) return;

            ExportFilePath = dialog.SelectedPath;
        }

        private void ExportLoggingSessions(object o)
        {
            if (string.IsNullOrWhiteSpace(ExportFilePath)) return;

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
            loggingSessionExporter.ExportLoggingSession(session, filepath);
        }

        private void ExportAverageSamples(LoggingSession session, string filepath)
        {
            var loggingSessionExporter = new LoggingSessionExporter();
            loggingSessionExporter.ExportAverageSamples(session, filepath, AverageQuantity);
        }

        private LoggingSession GetLoggingSessionFromId(int sessionId)
        {
            using (var context = new LoggingContext())
            {
                context.Configuration.AutoDetectChangesEnabled = false;

                var loggingSession = context.Sessions.Where(s => s.ID == sessionId)
                    .Include(s => s.DataSamples)
                    .FirstOrDefault();

                return loggingSession;
            }
        }
        #endregion
    }
}