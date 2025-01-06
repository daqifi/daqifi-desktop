using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Helpers;
using Daqifi.Desktop.Logger;
using System.IO;
using System.Text;
using Daqifi.Desktop.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
        public void ExportLoggingSession(LoggingSession loggingSession, string filepath, bool ExportRelativeTime)
        {
            try
            {
                var channelNames = loggingSession.DataSamples.Select(s => s.ChannelName).Distinct().ToList();
                var hasTimeStamps = loggingSession.DataSamples.Select(s => s.TimestampTicks).Distinct().Any();
                var samplesCount = loggingSession.DataSamples.Count;

                if (channelNames.Count == 0 || !hasTimeStamps) { return; }

                channelNames.Sort(new OrdinalStringComparer());

                // Create the header
                var sb = new StringBuilder();
                sb.Append("time").Append(Delimiter).Append(string.Join(Delimiter, channelNames.ToArray())).AppendLine();
                File.WriteAllText(filepath, sb.ToString());
                sb.Clear();

                var firstTimestampTicks = loggingSession.DataSamples.Min(s => s.TimestampTicks); // Capture first timestamp for relative time

                var count = 0;
                var pageSize = 10000 * channelNames.Count;
                while (count < samplesCount)
                {
                    var pagedSampleDictionary = loggingSession.DataSamples
                        .Select(s => new { s.TimestampTicks, s.ChannelName, s.Value })
                        .OrderBy(s => s.TimestampTicks)
                        .Skip(count)
                        .Take(pageSize)
                        .GroupBy(s => s.TimestampTicks)
                        .ToDictionary(s => s.Key, s => s.ToList());

                    foreach (var timestamp in pagedSampleDictionary.Keys)
                    {
                        var timeString = ExportRelativeTime
                    ? ((timestamp - firstTimestampTicks) / TimeSpan.TicksPerSecond).ToString("F3")
                    : new DateTime(timestamp).ToString("O");

                        sb.Append(timeString);

                        // Initialize a dictionary for all channels with null values
                        var sampleDictionary = channelNames.ToDictionary(channelName => channelName, channelName => (double?)null);
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
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Exception in ExportLoggingSession");
            }
        }

        public void ExportAverageSamples(LoggingSession session, string filepath, double averageQuantity, bool ExportRelativeTime)
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

                    foreach (var timestampTicks in rows.Keys)
                    {
                        var channelNumber = 0;
                        foreach (var value in rows[timestampTicks])
                        {
                            if (tempTotals.Count <= channelNumber) tempTotals.Add(0);
                            tempTotals[channelNumber] += value;
                            channelNumber++;
                        }

                        count++;

                        if (count % averageQuantity == 0)
                        {
                            string timeString = ExportRelativeTime
                                ? ((timestampTicks - firstTimestampTicks) / TimeSpan.TicksPerSecond).ToString("F3")
                                : new DateTime(timestampTicks).ToString("O");

                            sb.Append(timeString).Append(Delimiter);

                            foreach (var value in tempTotals)
                            {
                                sb.Append((value / averageQuantity).ToString("G")).Append(Delimiter);
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
    }
}
