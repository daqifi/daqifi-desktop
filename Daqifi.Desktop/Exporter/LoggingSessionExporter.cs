using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Helpers;
using Daqifi.Desktop.Logger;
using System.IO;
using System.Text;
using Daqifi.Desktop.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.IO;
using System.Text;

namespace Daqifi.Desktop.Exporter
{
    public class LoggingSessionExporter
    {
        private AppLogger AppLogger = AppLogger.Instance;
        private readonly string Delimiter = DaqifiSettings.Instance.CsvDelimiter;
        private readonly IDbContextFactory<LoggingContext> _loggingContext;
        public LoggingSessionExporter()
        {
            _loggingContext = App.ServiceProvider.GetRequiredService<IDbContextFactory<LoggingContext>>();
        }
      
        public void ExportLoggingSession(LoggingSession loggingSession, string filepath, bool exportRelativeTime, BackgroundWorker bw, int sessionIndex, int totalSessions)
        {
            try
            {
                var channelNames = loggingSession.DataSamples.Select(s => s.ChannelName).Distinct().ToList();
                var hasTimeStamps = loggingSession.DataSamples.Select(s => s.TimestampTicks).Distinct().Any();
                var samplesCount = loggingSession.DataSamples.Count;

                if (channelNames.Count == 0 || !hasTimeStamps) { return; }

                channelNames.Sort(new OrdinalStringComparer());

                var sb = new StringBuilder();
                sb.Append("time").Append(Delimiter).Append(string.Join(Delimiter, channelNames.ToArray())).AppendLine();
                File.WriteAllText(filepath, sb.ToString());
                sb.Clear();

                var firstTimestampTicks = loggingSession.DataSamples.Min(s => s.TimestampTicks); // Capture first timestamp for relative time

                var count = 0;
                var pageSize = 10000 * channelNames.Count;
                while (count < samplesCount)
                {
                    if (bw.CancellationPending)
                    {
                        AppLogger.Warning("Export operation cancelled by user.");
                        return;
                    }
                    var pagedSampleDictionary = loggingSession.DataSamples
                        .Select(s => new { s.TimestampTicks, s.ChannelName, s.Value })
                        .OrderBy(s => s.TimestampTicks)
                        .Skip(count)
                        .Take(pageSize)
                        .GroupBy(s => s.TimestampTicks)
                        .ToDictionary(s => s.Key, s => s.ToList());

                    foreach (var timestamp in pagedSampleDictionary.Keys)
                    {
                        var timeString = GetFormattedTime(timestamp, exportRelativeTime, firstTimestampTicks);
                        
                        sb.Append(timeString);

                        var sampleDictionary = channelNames.ToDictionary<string, string, double?>(channelName => channelName, channelName => null);

                        var samples = pagedSampleDictionary[timestamp];

                        foreach (var sample in samples)
                        {
                            sampleDictionary[sample.ChannelName] = sample.Value;
                        }

                        foreach (var sample in sampleDictionary)
                        {
                            sb.Append(Delimiter);
                            sb.Append(sample.Value);
                        }

                        sb.AppendLine();
                        File.AppendAllText(filepath, sb.ToString());
                        sb.Clear();
                    }
                    count += pageSize;
                    int sessionProgress = (int)((double)count / samplesCount * 100);
                    int overallProgress = (int)((sessionIndex + sessionProgress / 100.0) * (100.0 / totalSessions));
                    bw.ReportProgress(Math.Min(100, overallProgress), loggingSession.Name);

                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Exception in ExportLoggingSession");
            }
        }

        public void ExportAverageSamples(LoggingSession session, string filepath, double averageQuantity, bool exportRelativeTime, BackgroundWorker bw, int sessionIndex, int totalSessions)
        {
            try
            {
                using (var context = _loggingContext.CreateDbContext())
                {
                    context.ChangeTracker.AutoDetectChangesEnabled = false;
                    var loggingSession = context.Sessions.Find(session.ID);
                    var channelNames = context.Samples.AsNoTracking()
                                                      .Where(s => s.LoggingSessionID == loggingSession.ID)
                                                      .Select(s => s.ChannelName)
                                                      .Distinct()
                                                      .ToList();
                    var samples = context.Samples.AsNoTracking()
                                                 .Where(s => s.LoggingSessionID == loggingSession.ID)
                                                 .OrderBy(s => s.TimestampTicks)
                                                 .ToList();

                    if (channelNames.Count == 0 || samples.Count == 0) return;

                    var rows = new Dictionary<long, List<double>>();
                    var firstTimestampTicks = samples.First().TimestampTicks; // First timestamp for relative time

                    foreach (var sample in samples)
                    {
                        if (!rows.ContainsKey(sample.TimestampTicks))
                        {
                            rows[sample.TimestampTicks] = new List<double>();
                        }
                        rows[sample.TimestampTicks].Add(sample.Value);
                    }

                    // Create the header
                    var sb = new StringBuilder();
                    sb.Append("time").Append(Delimiter).Append(string.Join(Delimiter, channelNames)).AppendLine();

                    var count = 0;
                    var tempTotals = new List<double>();
                    int totalRows = rows.Count;

                    foreach (var timestamp in rows.Keys)
                    {
                        var channelNumber = 0;
                        foreach (var value in rows[timestamp])
                        {
                            if (tempTotals.Count <= channelNumber) tempTotals.Add(0);
                            tempTotals[channelNumber] += value;
                            channelNumber++;
                        }

                        count++;

                        if (count % averageQuantity == 0)
                        {
                            var timeString = GetFormattedTime(timestamp, exportRelativeTime, firstTimestampTicks);

                            sb.Append(timeString).Append(Delimiter);

                            foreach (var value in tempTotals)
                            {
                                sb.Append((value / averageQuantity).ToString("G")).Append(Delimiter);
                            }
                            sb.AppendLine();
                            tempTotals.Clear();
                        }
                        if (bw.WorkerReportsProgress)
                        {
                            int progressPercentage = (int)((double)count / totalRows * 100);
                            bw.ReportProgress(progressPercentage, new Tuple<int, int>(sessionIndex, totalSessions));
                        }
                        if (bw.CancellationPending)
                        {
                            return;
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
        
        private string GetFormattedTime(long timestampTicks, bool exportRelativeTime, long firstTimestampTicks)
        {
            return exportRelativeTime
                ? ((timestampTicks - firstTimestampTicks) / (double)TimeSpan.TicksPerMillisecond / 1000).ToString("F3")
                : new DateTime(timestampTicks).ToString("O");
        }
    }
}
