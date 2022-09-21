using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Helpers;
using Daqifi.Desktop.Logger;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Daqifi.Desktop.Exporter
{
    public class LoggingSessionExporter
    {
        private AppLogger AppLogger = AppLogger.Instance;

        public void ExportLoggingSession(LoggingSession loggingSession, string filepath)
        {
            try
            {
                var channelNames = loggingSession.DataSamples.Select(s => s.ChannelName).Distinct().ToList();
                var hasTimeStamps = loggingSession.DataSamples.Select(s => s.TimestampTicks).Distinct().Any();
                var samplesCount = loggingSession.DataSamples.Count;

                if (channelNames.Count == 0 || !hasTimeStamps) return;
                
                channelNames.Sort(new OrdinalStringComparer());

                // Create the header
                var sb = new StringBuilder();
                sb.Append("time,").Append(string.Join(",", channelNames.ToArray())).AppendLine();
                File.WriteAllText(filepath, sb.ToString());
                sb.Clear();

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
                        sb.Append(new DateTime(timestamp).ToString("O"));

                        // Create the template for samples dictionary
                        var sampleDictionary = channelNames.ToDictionary<string, string, double?>(channelName => channelName, channelName => null);
                        var samples = pagedSampleDictionary[timestamp];

                        foreach (var sample in samples)
                        {
                            sampleDictionary[sample.ChannelName] = sample.Value;
                        }

                        foreach (var sample in sampleDictionary)
                        {
                            sb.Append(",");
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

        public void ExportAverageSamples(LoggingSession session, string filepath, double averageQuantity)
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

                    // Create the header
                    var sb = new StringBuilder();
                    sb.Append("time,").Append(string.Join(",", channelNames.ToArray())).AppendLine();

                    var count = 0;
                    var tempTotals = new List<double>();

                    foreach (var row in rows.Keys)
                    {
                        var channelNumber = 0;
                        foreach (var value in rows[row])
                        {
                            if (tempTotals.Count - 1 < channelNumber) tempTotals.Add(0);
                            tempTotals[channelNumber] += value;
                            channelNumber++;
                        }

                        count++;

                        if (count % averageQuantity == 0)
                        {
                            // Average and write to file
                            sb.Append(row).Append(",");
                            foreach (var value in tempTotals)
                            {
                                sb.Append(value / averageQuantity).Append(",");
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
