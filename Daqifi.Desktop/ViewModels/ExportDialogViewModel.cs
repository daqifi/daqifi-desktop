using Daqifi.Desktop.Commands;
using Daqifi.Desktop.Helpers;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.Loggers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Input;
using Daqifi.Desktop.Exporter;

namespace Daqifi.Desktop.ViewModels
{
    public class ExportDialogViewModel : ObservableObject
    {
        #region Private Variables
        private readonly ICollection<LoggingSession> _sessions;
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
                NotifyPropertyChanged("ExportFilePath");
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool ExportAllSelected
        {
            get => _exportAllSelected;
            set
            {
                _exportAllSelected = value;
                NotifyPropertyChanged("ExportAllSelected");
            }
        }

        public bool ExportAverageSelected
        {
            get => _exportAverageSelected;
            set
            {
                _exportAverageSelected = value;
                NotifyPropertyChanged("ExportAverageSelected");
            }
        }

        public int AverageQuantity
        {
            get => _averageQuantity;
            set
            {
                _averageQuantity = value;
                NotifyPropertyChanged("AverageQuantity");
            }

        }
        #endregion

        #region Command Properties
        public ICommand BrowseExportPathCommand { get; private set; }
        private bool CanBrowseExportPath(object o)
        {
            return true;
        }

        public ICommand ExportSessionCommand { get; private set; }
        private bool CanExportSession(object o)
        {
            return true;
        }
        #endregion

        #region Constructor
        public ExportDialogViewModel(LoggingSession session)
        {
            _sessions = new List<LoggingSession>() { session };
            ExportSessionCommand = new DelegateCommand(ExportSingleSession, CanExportSession);
            BrowseExportPathCommand = new DelegateCommand(BrowseExportPath, CanBrowseExportPath);
        }

        public ExportDialogViewModel(ICollection<LoggingSession> sessions)
        {
            _sessions = sessions;
            ExportSessionCommand = new DelegateCommand(ExportAllSessions, CanExportSession);
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

        private void ExportSingleSession(object o)
        {
            if (string.IsNullOrWhiteSpace(ExportFilePath)) return;

            var bw = new BackgroundWorker();
            bw.DoWork += delegate
            {
                foreach (var session in _sessions)
                {
                    var filepath = ExportFilePath;
                    if (ExportAllSelected)
                    {
                        ExportAllSamples(session, filepath);
                    }
                    else if (ExportAverageSelected)
                    {
                        ExportAverageSamples(session, filepath);
                    }
                }
            };

            bw.RunWorkerAsync();
        }

        private void ExportAllSessions(object o)
        {
            if (string.IsNullOrWhiteSpace(ExportFilePath)) return;

            var bw = new BackgroundWorker();
            bw.DoWork += delegate
            {
                foreach (var session in _sessions)
                {
                    var filepath = ExportFilePath + "\\" + session.Name + ".csv";
                    if (ExportAllSelected)
                    {
                        ExportAllSamples(session, filepath);
                    }
                    else if (ExportAverageSelected)
                    {
                        ExportAverageSamples(session, filepath);
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
        #endregion
    }
}