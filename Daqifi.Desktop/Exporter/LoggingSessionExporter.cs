using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Helpers;
using Daqifi.Desktop.Logger;
using System.IO;
using System.Text;
using Daqifi.Desktop.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
        public void ExportLoggingSession(LoggingSession loggingSession, string filepath)
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

                // Create the header
                var sb = new StringBuilder();
                sb.Append("Time").Append(Delimiter).Append(string.Join(Delimiter, channelNames)).AppendLine();
                File.WriteAllText(filepath, sb.ToString());
                sb.Clear();

                var count = 0;
                var pageSize = 10000 * channelNames.Count;
                while (count < samplesCount)
                {
                    var pagedSampleDictionary = loggingSession.DataSamples
                        .Select(s => new { s.TimestampTicks, DeviceChannel = $"{s.DeviceName}:{s.DeviceSerialNo}:{s.ChannelName}", s.Value })
                        .OrderBy(s => s.TimestampTicks)
                        .Skip(count)
                        .Take(pageSize)
                        .GroupBy(s => s.TimestampTicks)
                        .ToDictionary(s => s.Key, s => s.ToList());

                    foreach (var timestamp in pagedSampleDictionary.Keys)
                    {
                        sb.Append(new DateTime(timestamp).ToString("O"));

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
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Exception in ExportLoggingSession");
            }
        }

        public void ExportAverageSamples(LoggingSession session, string filepath, double averageQuantity)
        {
            try
            {
                using (var context = _loggingContext.CreateDbContext())
                {
                    context.ChangeTracker.AutoDetectChangesEnabled = false;
                    var loggingSession = context.Sessions.Find(session.ID);

                    // Extract channel names in "Device:Channel" format
                    var channelNames = context.Samples
                        .AsNoTracking()
                        .Where(s => s.LoggingSessionID == loggingSession.ID)
                        .Select(s => $"{s.DeviceName}:{s.DeviceSerialNo}:{s.ChannelName}")
                        .Distinct()
                        .ToList();

                    var samples = context.Samples
                        .AsNoTracking()
                        .Where(s => s.LoggingSessionID == loggingSession.ID)
                        .Select(s => s);

                    var rows = new Dictionary<DateTime, List<KeyValuePair<string, double>>>();
                    foreach (var sample in samples)
                    {
                        var timestamp = new DateTime(sample.TimestampTicks);
                        if (!rows.ContainsKey(timestamp))
                        {
                            rows[timestamp] = new List<KeyValuePair<string, double>>();
                        }
                        rows[timestamp].Add(new KeyValuePair<string, double>($"{sample.DeviceName}:{sample.DeviceSerialNo}:{sample.ChannelName}", sample.Value));
                    }

                    // Create the header
                    var sb = new StringBuilder();
                    sb.Append("Time").Append(Delimiter).Append(string.Join(Delimiter, channelNames)).AppendLine();

                    var count = 0;
                    var tempTotals = channelNames.ToDictionary(name => name, _ => 0.0);
                    var tempCounts = channelNames.ToDictionary(name => name, _ => 0);

                    foreach (var row in rows.Keys.OrderBy(t => t))
                    {
                        foreach (var kvp in rows[row])
                        {
                            tempTotals[kvp.Key] += kvp.Value;
                            tempCounts[kvp.Key]++;
                        }

                        count++;

                        if (count % averageQuantity == 0)
                        {
                            sb.Append(row.ToString("O")).Append(Delimiter);
                            foreach (var channelName in channelNames)
                            {
                                var average = tempCounts[channelName] > 0 ? tempTotals[channelName] / tempCounts[channelName] : (double?)null;
                                sb.Append(average?.ToString("G") ?? string.Empty).Append(Delimiter);
                            }
                            sb.AppendLine();

                            tempTotals = channelNames.ToDictionary(name => name, _ => 0.0);
                            tempCounts = channelNames.ToDictionary(name => name, _ => 0);
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
