using Daqifi.Desktop.Commands;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.Loggers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Input;
using Daqifi.Desktop.Helpers;

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
            try
            {
                using (var context = new LoggingContext())
                {
                    context.Configuration.AutoDetectChangesEnabled = false;
                    var loggingSession = context.Sessions.Find(session.ID);

                    if (loggingSession == null) return;

                    var samples = context.Samples.AsNoTracking().Where(s => s.LoggingSessionID == loggingSession.ID).Select(s => s);
                    var channelNames = samples.Select(s => s.ChannelName).Distinct().ToArray();
                    var timestampticks = samples.Select(s => s.TimestampTicks).Distinct().ToArray();

                    var rows = GetExportDataStructure(channelNames, timestampticks);

                    foreach (var sample in samples)
                    {
                        rows[sample.TimestampTicks][sample.ChannelName] = sample.Value;
                    }

                    //Create the heeader
                    var sb = new StringBuilder();
                    sb.Append("time,").Append(string.Join(",", channelNames.ToArray())).AppendLine();

                    foreach (var timestampTicks in rows.Keys)
                    {
                        sb.Append(new DateTime(timestampTicks)).Append(",");

                        foreach (var channel in channelNames)
                        {
                            var value = rows[timestampTicks][channel];
                            if (value == null)
                            {
                                sb.Append(",");
                            }
                            else
                            {
                                sb.Append(value.Value).Append(",");
                            }
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

        private Dictionary<long, Dictionary<string, double?>> GetExportDataStructure(string[]channelNames, long[] timestampTicks)
        {
            // Define data structure
            var rows = new Dictionary<long, Dictionary<string, double?>>();

            // Sort the channels in a natural sort order
            Array.Sort(channelNames, new OrdinalStringComparer());

            // Populate skeleton of data structure
            foreach (var timestamptick in timestampTicks)
            {
                // For each timestamp, create a placeholder for each channel
                var channelValuesAtTimestamp = new Dictionary<string, double?>();
                foreach (var channel in channelNames)
                {
                    channelValuesAtTimestamp.Add(channel, null);
                }

                rows.Add(timestamptick, channelValuesAtTimestamp);
            }

            return rows;
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