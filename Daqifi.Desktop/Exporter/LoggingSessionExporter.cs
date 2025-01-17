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
                var channelNames = loggingSession.DataSamples
                    .Select(s => $"{s.DeviceName}:{s.DeviceSerialNo}:{s.ChannelName}")
                    .Distinct()
                    .ToList();

                var hasTimeStamps = loggingSession.DataSamples.Select(s => s.TimestampTicks).Distinct().Any();
                var samplesCount = loggingSession.DataSamples.Count;

                if (channelNames.Count == 0 || !hasTimeStamps) { return; }

                channelNames.Sort(new OrdinalStringComparer());

                var sb = new StringBuilder();
                sb.Append(exportRelativeTime ? "Relative Time (s)" : "Time").Append(Delimiter).Append(string.Join(Delimiter, channelNames)).AppendLine();
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
                        .Select(s => new { s.TimestampTicks, DeviceChannel = $"{s.DeviceName}:{s.DeviceSerialNo}:{s.ChannelName}", s.Value })
                        .OrderBy(s => s.TimestampTicks)
                        .Skip(count)
                        .Take(pageSize)
                        .GroupBy(s => s.TimestampTicks)
                        .ToDictionary(s => s.Key, s => s.ToList());

                    foreach (var timestamp in pagedSampleDictionary.Keys)
                    {
                        var timeString = exportRelativeTime
                            ? ((timestamp - firstTimestampTicks) / (double)TimeSpan.TicksPerSecond).ToString("F3")  // Relative time in seconds
                            : new DateTime(timestamp).ToString("O");  

                        sb.Append(timeString);

                        // Create the template for samples dictionary 
                        var sampleDictionary = channelNames.ToDictionary(channel => channel, channel => (double?)null);
                        var samples = pagedSampleDictionary[timestamp];

                        foreach (var sample in samples)
                        {
                            sampleDictionary[sample.DeviceChannel] = sample.Value;
                        }

                        foreach (var sample in sampleDictionary)
                        {
                            sb.Append(Delimiter);
                            sb.Append(sample.Value.HasValue ? sample.Value.Value.ToString("G") : string.Empty);
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

                    var channelNames = context.Samples
                        .AsNoTracking()
                        .Where(s => s.LoggingSessionID == loggingSession.ID)
                        .Select(s => $"{s.DeviceName}:{s.DeviceSerialNo}:{s.ChannelName}")
                        .Distinct()
                        .ToList();

                    var samples = context.Samples
                        .AsNoTracking()
                        .Where(s => s.LoggingSessionID == loggingSession.ID);

                    var rows = new Dictionary<long, List<KeyValuePair<string, double>>>();
                    foreach (var sample in samples)
                    {
                        if (!rows.ContainsKey(sample.TimestampTicks))
                        {
                            rows[sample.TimestampTicks] = new List<KeyValuePair<string, double>>();
                        }
                        rows[sample.TimestampTicks].Add(new KeyValuePair<string, double>($"{sample.DeviceName}:{sample.DeviceSerialNo}:{sample.ChannelName}", sample.Value));
                    }

                    var sb = new StringBuilder();
                    sb.Append(exportRelativeTime ? "Relative Time (s)" : "Time").Append(Delimiter).Append(string.Join(Delimiter, channelNames)).AppendLine();

                    var tempTotals = channelNames.ToDictionary(name => name, _ => 0.0);
                    var tempCounts = channelNames.ToDictionary(name => name, _ => 0);

                    long firstTimestampTicks = rows.Keys.Min();
                    int count = 0;
                    int totalRows = rows.Count;

                    foreach (var timestamp in rows.Keys.OrderBy(t => t))
                    {
                        foreach (var kvp in rows[timestamp])
                        {
                            tempTotals[kvp.Key] += kvp.Value;
                            tempCounts[kvp.Key]++;
                        }

                        count++;

                        if (count % averageQuantity == 0)
                        {
                            string timeString = exportRelativeTime
                                ? ((timestamp - firstTimestampTicks) / (double)TimeSpan.TicksPerSecond).ToString("F3")
                                : new DateTime(timestamp).ToString("O");

                            sb.Append(timeString).Append(Delimiter);
                            foreach (var channelName in channelNames)
                            {
                                var average = tempCounts[channelName] > 0 ? tempTotals[channelName] / tempCounts[channelName] : (double?)null;
                                sb.Append(average?.ToString("G") ?? string.Empty).Append(Delimiter);
                            }
                            sb.Length--;
                            sb.AppendLine();

                            tempTotals = channelNames.ToDictionary(name => name, _ => 0.0);
                            tempCounts = channelNames.ToDictionary(name => name, _ => 0);
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
                AppLogger.Error(ex, "Failed in ExportAverageSamples");
            }
        }
    }
}
