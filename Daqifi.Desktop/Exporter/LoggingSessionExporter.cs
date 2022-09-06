using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Helpers;
using Daqifi.Desktop.Logger;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;

namespace Daqifi.Desktop.Exporter
{
    public class LoggingSessionExporter
    {
        public AppLogger AppLogger = AppLogger.Instance;

        public void ExportLoggingSession(LoggingSession loggingSession, string filepath)
        {
            try
            {
                var channelNames = loggingSession.DataSamples.Select(s => s.ChannelName).Distinct().ToArray();
                var timestampticks = loggingSession.DataSamples.Select(s => s.TimestampTicks).Distinct().ToArray();

                if (channelNames.Length == 0 || timestampticks.Length == 0) return;

                var rows = GetExportDataStructure(channelNames, timestampticks);

                foreach (var sample in loggingSession.DataSamples)
                {
                    rows[sample.TimestampTicks][sample.ChannelName] = sample.Value;
                }

                // Create the heeader
                var sb = new StringBuilder();
                sb.Append("time,").Append(string.Join(",", channelNames.ToArray())).AppendLine();

                var lastChannelName = channelNames.Last();

                // For each time period
                foreach (var timestampTicks in rows.Keys)
                {
                    sb.Append(new DateTime(timestampTicks).ToString("O")).Append(",");

                    // Get all the channels
                    foreach (var channel in channelNames)
                    {
                        var value = rows[timestampTicks][channel];
                    
                        if (value != null)
                        {
                            sb.Append(value.Value);
                        }

                        if (!channel.Equals(lastChannelName))
                        {
                            sb.Append(",");
                        }
                    }

                    sb.AppendLine();
                    File.AppendAllText(filepath, sb.ToString());
                    sb.Clear();
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

        private Dictionary<long, Dictionary<string, double?>> GetExportDataStructure(string[] channelNames, long[] timestampTicks)
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
    }
}
