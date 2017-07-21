using Daqifi.Desktop.Commands;
using Daqifi.Desktop.Logger;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Input;
using Daqifi.Desktop.Loggers;

namespace Daqifi.Desktop.ViewModels
{
    class ExportDialogViewModel : ObservableObject
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
            get { return _exportFilePath; }
            set 
            { 
                _exportFilePath = value;
                NotifyPropertyChanged("ExportFilePath");
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool ExportAllSelected
        {
            get { return _exportAllSelected; }
            set
            {
                _exportAllSelected = value;
                NotifyPropertyChanged("ExportAllSelected");
            }
        }

        public bool ExportAverageSelected
        {
            get { return _exportAverageSelected; }
            set
            {
                _exportAverageSelected = value;
                NotifyPropertyChanged("ExportAverageSelected");
            }
        }

        public int AverageQuantity
        {
            get { return _averageQuantity; }
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

        private void BrowseExportPath(object o)
        {
            Microsoft.Win32.SaveFileDialog dlg = new Microsoft.Win32.SaveFileDialog
            {
                DefaultExt = ".csv",
                Filter = "Log File|*.csv"
            };

            bool? result = dlg.ShowDialog();

            if (result == false) return;

            ExportFilePath = dlg.FileName;
        }

        private void BrowseExportDirectory(object o)
        {
            System.Windows.Forms.FolderBrowserDialog dlg = new System.Windows.Forms.FolderBrowserDialog();

            System.Windows.Forms.DialogResult result = dlg.ShowDialog();

            if (result == System.Windows.Forms.DialogResult.Cancel) return;

            ExportFilePath = dlg.SelectedPath;
        }

        private void ExportSingleSession(object o)
        {
            if (string.IsNullOrWhiteSpace(ExportFilePath)) return;

            var bw = new BackgroundWorker();
            bw.DoWork += delegate
            {
                foreach (var session in _sessions)
                {
                    string filepath = ExportFilePath;
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
                foreach (LoggingSession session in _sessions)
                {
                    string filepath = ExportFilePath + "\\" + session.Name + ".csv";
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
            try
            {
                using (var context = new LoggingContext())
                {
                    context.Configuration.AutoDetectChangesEnabled = false;
                    LoggingSession loggingSession = context.Sessions.Find(session.ID);

                    if (loggingSession == null) return;

                    var channelNames = context.Samples.AsNoTracking().Where(s => s.LoggingSessionID == loggingSession.ID).Select(s => s.ChannelName).Distinct();
                    var samples = context.Samples.AsNoTracking().Where(s => s.LoggingSessionID == loggingSession.ID).Select(s => s);

                    var rows = new Dictionary<DateTime, List<double>>();
                    foreach (var sample in samples)
                    {
                        //Check if we have already seen this timestamp, if not, add it's key.
                        var sampleDateTime = new DateTime(sample.TimestampTicks);
                        if (!rows.Keys.Contains(sampleDateTime)) rows.Add(sampleDateTime,new List<double>());
                        rows[new DateTime(sample.TimestampTicks)].Add(sample.Value);
                    }

                    //Create the heeader
                    var sb = new StringBuilder();
                    sb.Append("time,").Append(string.Join(",", channelNames.ToArray())).AppendLine();

                    foreach (DateTime row in rows.Keys)
                    {
                        sb.Append(row).Append(",");
                        foreach (double value in rows[row])
                        {
                            sb.Append(value).Append(",");
                        }
                        sb.AppendLine();
                    }

                    File.WriteAllText(filepath, sb.ToString());
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Failed in ExportLoggingSession");
            }
        }

        private void ExportAverageSamples(LoggingSession session, string filepath)
        {
            try
            {
                using (var context = new LoggingContext())
                {
                    context.Configuration.AutoDetectChangesEnabled = false;
                    var loggingSession = context.Sessions.Find(session.ID);
                    var channelNames = context.Samples.AsNoTracking().Where(s => s.LoggingSessionID == loggingSession.ID).Select(s => s.ChannelName).Distinct();
                    var samples = context.Samples.AsNoTracking().Where(s => s.LoggingSessionID == loggingSession.ID).Select(s => s);

                    var rows = new Dictionary<DateTime, List<double>>();
                    foreach (var sample in samples)
                    {
                        if (!rows.Keys.Contains(new DateTime(sample.TimestampTicks)))
                        {
                            rows.Add(new DateTime(sample.TimestampTicks), new List<double>());
                        }

                        rows[new DateTime(sample.TimestampTicks)].Add(sample.Value);
                    }

                    //Create the heeader
                    var sb = new StringBuilder();
                    sb.Append("time,").Append(string.Join(",", channelNames.ToArray())).AppendLine();

                    int count = 0;
                    var tempTotals = new List<double>();

                    foreach (var row in rows.Keys)
                    {
                        int channelNumber = 0;
                        foreach (double value in rows[row])
                        {
                            if (tempTotals.Count - 1 < channelNumber) tempTotals.Add(0);
                            tempTotals[channelNumber] += value;
                            channelNumber++;
                        }

                        count++;

                        if (count % AverageQuantity == 0)
                        {
                            //Average and write to file
                            sb.Append(row).Append(",");
                            foreach (double value in tempTotals)
                            {
                                sb.Append(value / AverageQuantity).Append(",");
                            }
                            sb.AppendLine();
                            tempTotals.Clear();
                        }
                    }
                    File.WriteAllText(filepath, sb.ToString());
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Failed in ExportLoggingSession");
            }
        }
        #endregion
    }
}