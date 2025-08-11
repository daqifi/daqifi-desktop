using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Helpers;
using Daqifi.Desktop.Logger;
using System.IO;
using System.Text;
using Daqifi.Desktop.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;

namespace Daqifi.Desktop.Exporter;

public record SampleData(long TimestampTicks, string DeviceChannel, double Value);

public class OptimizedLoggingSessionExporter
{
    private readonly AppLogger _appLogger = AppLogger.Instance;
    private readonly string _delimiter = DaqifiSettings.Instance.CsvDelimiter;
    private readonly IDbContextFactory<LoggingContext> _loggingContext;
    private const int BATCH_SIZE = 50000; // Process samples in batches
    private const int BUFFER_SIZE = 1024 * 1024; // 1MB buffer for file writes

    public OptimizedLoggingSessionExporter()
    {
        if (App.ServiceProvider != null)
        {
            _loggingContext = App.ServiceProvider.GetRequiredService<IDbContextFactory<LoggingContext>>();
        }
    }

    public OptimizedLoggingSessionExporter(IDbContextFactory<LoggingContext> loggingContext)
    {
        _loggingContext = loggingContext;
    }

    public void ExportLoggingSession(LoggingSession loggingSession, string filepath, bool exportRelativeTime, BackgroundWorker bw, int sessionIndex, int totalSessions)
    {
        try
        {
            // Check if we have in-memory data (for tests) or need to query database
            if (loggingSession.DataSamples?.Any() == true)
            {
                // Use optimized in-memory processing for test scenarios
                ExportFromMemory(loggingSession, filepath, exportRelativeTime, bw, sessionIndex, totalSessions);
            }
            else if (_loggingContext != null)
            {
                // Use database streaming for production scenarios
                ExportFromDatabase(loggingSession, filepath, exportRelativeTime, bw, sessionIndex, totalSessions);
            }
            else
            {
                _appLogger.Warning("No data source available for export");
            }
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Exception in OptimizedExportLoggingSession");
        }
    }

    private void ExportFromMemory(LoggingSession loggingSession, string filepath, bool exportRelativeTime, BackgroundWorker bw, int sessionIndex, int totalSessions)
    {
        var channelNames = loggingSession.DataSamples
            .Select(s => $"{s.DeviceName}:{s.DeviceSerialNo}:{s.ChannelName}")
            .Distinct()
            .OrderBy(name => name, StringComparer.Ordinal) // Use StringComparer.Ordinal for consistent sorting
            .ToList();

        var hasTimestamps = loggingSession.DataSamples.Any(); // Check if we have samples, not if timestamps > 0
        if (channelNames.Count == 0 || !hasTimestamps)
        {
            return;
        }

        var firstTimestamp = loggingSession.DataSamples.Min(s => s.TimestampTicks);
        
        // Write header efficiently
        using var writer = new StreamWriter(filepath, false, Encoding.UTF8, BUFFER_SIZE);
        WriteHeaderToWriter(writer, channelNames, exportRelativeTime);

        // Process data without creating intermediate collections
        WriteMemoryDataDirectly(writer, loggingSession.DataSamples, channelNames, firstTimestamp, exportRelativeTime, bw, sessionIndex, totalSessions);
    }

    private void ExportFromDatabase(LoggingSession loggingSession, string filepath, bool exportRelativeTime, BackgroundWorker bw, int sessionIndex, int totalSessions)
    {
        // Get channel names and basic info without loading all data
        var channelInfo = GetChannelInfoFromDatabase(loggingSession);
        if (channelInfo.channelNames.Count == 0 || !channelInfo.hasTimestamps)
        {
            return;
        }

        // Write header
        WriteHeader(filepath, channelInfo.channelNames, exportRelativeTime);

        // Process data in streaming fashion using database queries
        StreamDataToFile(loggingSession, filepath, channelInfo, exportRelativeTime, bw, sessionIndex, totalSessions);
    }

    private (List<string> channelNames, bool hasTimestamps, int samplesCount, long firstTimestamp) GetChannelInfoFromDatabase(LoggingSession loggingSession)
    {
        using var context = _loggingContext.CreateDbContext();
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        var query = context.Samples
            .AsNoTracking()
            .Where(s => s.LoggingSessionID == loggingSession.ID);

        // Get channel names with EF Core compatible ordering
        var channelNames = query
            .Select(s => $"{s.DeviceName}:{s.DeviceSerialNo}:{s.ChannelName}")
            .Distinct()
            .OrderBy(name => name) // Use default string ordering instead of OrdinalStringComparer
            .ToList();

        // Get min timestamp and count in a single query to avoid logic errors and reduce roundtrips
        var stats = query
            .GroupBy(s => 1) // Dummy groupby to use aggregate functions
            .Select(g => new { 
                MinTimestamp = g.Min(s => (long?)s.TimestampTicks) ?? 0L,
                Count = g.Count()
            })
            .FirstOrDefault();

        var firstTimestamp = stats?.MinTimestamp ?? 0L;
        var samplesCount = stats?.Count ?? 0;

        return (channelNames, samplesCount > 0, samplesCount, firstTimestamp);
    }

    private void WriteHeaderToWriter(StreamWriter writer, List<string> channelNames, bool exportRelativeTime)
    {
        var header = exportRelativeTime ? "Relative Time (s)" : "Time";
        writer.Write(header);
        foreach (var channelName in channelNames)
        {
            writer.Write(_delimiter);
            writer.Write(channelName);
        }
        writer.WriteLine();
    }

    private void WriteMemoryDataDirectly(StreamWriter writer, ICollection<DataSample> dataSamples, List<string> channelNames, 
        long firstTimestamp, bool exportRelativeTime, BackgroundWorker bw, int sessionIndex, int totalSessions)
    {
        var sb = new StringBuilder(1024 * 4);
        var processedSamples = 0;
        var totalSamples = dataSamples.Count;

        // Stream process by timestamp to avoid materializing all groups in memory
        var orderedSamples = dataSamples.OrderBy(s => s.TimestampTicks);
        
        long? currentTimestamp = null;
        var timestampBucket = new List<DataSample>();
        
        foreach (var sample in orderedSamples)
        {
            if (bw.CancellationPending)
            {
                _appLogger.Warning("Export operation cancelled by user.");
                return;
            }

            // Check if we've moved to a new timestamp
            if (currentTimestamp.HasValue && sample.TimestampTicks != currentTimestamp.Value)
            {
                // Write the completed timestamp group
                WriteTimestampGroup(writer, sb, timestampBucket, channelNames, firstTimestamp, exportRelativeTime);
                processedSamples += timestampBucket.Count;
                
                // Update progress periodically
                if (processedSamples % 5000 == 0)
                {
                    var sessionProgress = Math.Min(100, (int)((double)processedSamples / totalSamples * 100));
                    var overallProgress = (int)((sessionIndex + sessionProgress / 100.0) * (100.0 / totalSessions));
                    bw.ReportProgress(overallProgress, "Exporting");
                }

                timestampBucket.Clear();
            }

            currentTimestamp = sample.TimestampTicks;
            timestampBucket.Add(sample);
        }

        // Write the final timestamp group
        if (timestampBucket.Count > 0)
        {
            WriteTimestampGroup(writer, sb, timestampBucket, channelNames, firstTimestamp, exportRelativeTime);
            processedSamples += timestampBucket.Count;
            
            // Final progress update
            var finalProgress = (int)((sessionIndex + 1.0) * (100.0 / totalSessions));
            bw.ReportProgress(finalProgress, "Exporting");
        }
    }

    private void WriteTimestampGroup(StreamWriter writer, StringBuilder sb, List<DataSample> timestampSamples, 
        List<string> channelNames, long firstTimestamp, bool exportRelativeTime)
    {
        if (!timestampSamples.Any()) return;
        
        var timestamp = timestampSamples.First().TimestampTicks;
        var timeString = exportRelativeTime
            ? ((timestamp - firstTimestamp) / (double)TimeSpan.TicksPerSecond).ToString("F3")
            : new DateTime(timestamp).ToString("O");

        sb.Clear();
        sb.Append(timeString);

        // Create lookup for this timestamp's samples - handle duplicates by taking the last value
        var sampleLookup = new Dictionary<string, double>();
        foreach (var sample in timestampSamples)
        {
            var channelKey = $"{sample.DeviceName}:{sample.DeviceSerialNo}:{sample.ChannelName}";
            sampleLookup[channelKey] = sample.Value; // Overwrite duplicates like original implementation
        }

        foreach (var channelName in channelNames)
        {
            sb.Append(_delimiter);
            if (sampleLookup.TryGetValue(channelName, out var value))
            {
                sb.Append(value.ToString("G"));
            }
        }

        sb.AppendLine();
        writer.Write(sb.ToString());
    }

    private void WriteHeader(string filepath, List<string> channelNames, bool exportRelativeTime)
    {
        using var writer = new StreamWriter(filepath, false, Encoding.UTF8, BUFFER_SIZE);
        var header = exportRelativeTime ? "Relative Time (s)" : "Time";
        writer.Write(header);
        foreach (var channelName in channelNames)
        {
            writer.Write(_delimiter);
            writer.Write(channelName);
        }
        writer.WriteLine();
    }

    private void StreamDataToFile(LoggingSession loggingSession, string filepath, 
        (List<string> channelNames, bool hasTimestamps, int samplesCount, long firstTimestamp) channelInfo,
        bool exportRelativeTime, BackgroundWorker bw, int sessionIndex, int totalSessions)
    {
        using var context = _loggingContext.CreateDbContext();
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        using var writer = new StreamWriter(filepath, true, Encoding.UTF8, BUFFER_SIZE);
        var sb = new StringBuilder(1024 * 4);
        
        var processedSamples = 0;
        long? lastProcessedTimestamp = null;

        while (processedSamples < channelInfo.samplesCount)
        {
            if (bw.CancellationPending)
            {
                _appLogger.Warning("Export operation cancelled by user.");
                return;
            }

            // Get distinct timestamps in batch to ensure we don't split timestamp groups
            var timestampBatch = context.Samples
                .AsNoTracking()
                .Where(s => s.LoggingSessionID == loggingSession.ID &&
                          (lastProcessedTimestamp == null || s.TimestampTicks > lastProcessedTimestamp.Value))
                .OrderBy(s => s.TimestampTicks)
                .Select(s => s.TimestampTicks)
                .Distinct()
                .Take(BATCH_SIZE / channelInfo.channelNames.Count) // Estimate timestamps per batch
                .ToList();

            if (!timestampBatch.Any())
                break;

            // Process each timestamp completely to maintain data integrity
            foreach (var timestamp in timestampBatch)
            {
                if (bw.CancellationPending)
                {
                    _appLogger.Warning("Export operation cancelled by user.");
                    return;
                }

                // Get all samples for this specific timestamp
                var timestampSamples = context.Samples
                    .AsNoTracking()
                    .Where(s => s.LoggingSessionID == loggingSession.ID && s.TimestampTicks == timestamp)
                    .Select(s => new SampleData(s.TimestampTicks, $"{s.DeviceName}:{s.DeviceSerialNo}:{s.ChannelName}", s.Value))
                    .ToList();

                // Write complete timestamp group
                WriteCompleteTimestampRow(writer, sb, timestampSamples, channelInfo.channelNames, 
                    channelInfo.firstTimestamp, exportRelativeTime);

                processedSamples += timestampSamples.Count;
                lastProcessedTimestamp = timestamp;
            }

            // Update progress
            var sessionProgress = Math.Min(100, (int)((double)processedSamples / channelInfo.samplesCount * 100));
            var overallProgress = (int)((sessionIndex + sessionProgress / 100.0) * (100.0 / totalSessions));
            bw.ReportProgress(overallProgress, loggingSession.Name);
        }
    }

    private void WriteCompleteTimestampRow(StreamWriter writer, StringBuilder sb, List<SampleData> timestampSamples,
        List<string> channelNames, long firstTimestamp, bool exportRelativeTime)
    {
        if (!timestampSamples.Any()) return;

        var timestamp = timestampSamples.First().TimestampTicks;
        var timeString = exportRelativeTime
            ? ((timestamp - firstTimestamp) / (double)TimeSpan.TicksPerSecond).ToString("F3")
            : new DateTime(timestamp).ToString("O");

        sb.Clear();
        sb.Append(timeString);

        // Create lookup for fast channel value retrieval - handle duplicates by taking the last value
        var sampleLookup = new Dictionary<string, double>();
        foreach (var sample in timestampSamples)
        {
            sampleLookup[sample.DeviceChannel] = sample.Value; // Overwrite duplicates like original implementation
        }

        foreach (var channelName in channelNames)
        {
            sb.Append(_delimiter);
            if (sampleLookup.TryGetValue(channelName, out var value))
            {
                sb.Append(value.ToString("G"));
            }
        }

        sb.AppendLine();
        writer.Write(sb.ToString());
    }


    public void ExportAverageSamples(LoggingSession session, string filepath, double averageQuantity, 
        bool exportRelativeTime, BackgroundWorker bw, int sessionIndex, int totalSessions)
    {
        try
        {
            using var context = _loggingContext.CreateDbContext();
            context.ChangeTracker.AutoDetectChangesEnabled = false;

            var channelNames = context.Samples
                .AsNoTracking()
                .Where(s => s.LoggingSessionID == session.ID)
                .Select(s => $"{s.DeviceName}:{s.DeviceSerialNo}:{s.ChannelName}")
                .Distinct()
                .OrderBy(name => name)
                .ToList();

            if (!channelNames.Any())
                return;

            using var writer = new StreamWriter(filepath, false, Encoding.UTF8, BUFFER_SIZE);
            
            // Write header
            var header = exportRelativeTime ? "Relative Time (s)" : "Time";
            writer.Write(header);
            foreach (var channelName in channelNames)
            {
                writer.Write(_delimiter);
                writer.Write(channelName);
            }
            writer.WriteLine();

            // Stream and process averages in batches
            StreamAverageData(context, session.ID, writer, channelNames, averageQuantity, exportRelativeTime, bw, sessionIndex, totalSessions);
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Failed in OptimizedExportAverageSamples");
        }
    }

    private void StreamAverageData(LoggingContext context, int sessionId, StreamWriter writer, 
        List<string> channelNames, double averageQuantity, bool exportRelativeTime, 
        BackgroundWorker bw, int sessionIndex, int totalSessions)
    {
        var tempTotals = channelNames.ToDictionary(name => name, _ => 0.0);
        var tempCounts = channelNames.ToDictionary(name => name, _ => 0);
        var sb = new StringBuilder(1024 * 4);

        long? firstTimestampTicks = null;
        var count = 0;
        var totalProcessed = 0;

        // Process samples in streaming fashion
        var samplesQuery = context.Samples
            .AsNoTracking()
            .Where(s => s.LoggingSessionID == sessionId)
            .OrderBy(s => s.TimestampTicks)
            .Select(s => new SampleData(s.TimestampTicks, $"{s.DeviceName}:{s.DeviceSerialNo}:{s.ChannelName}", s.Value));

        var totalSamples = context.Samples
            .AsNoTracking()
            .Where(s => s.LoggingSessionID == sessionId)
            .Count();

        foreach (var sample in samplesQuery)
        {
            if (!firstTimestampTicks.HasValue)
                firstTimestampTicks = sample.TimestampTicks;

            tempTotals[sample.DeviceChannel] += sample.Value;
            tempCounts[sample.DeviceChannel]++;
            count++;
            totalProcessed++;

            if (count >= averageQuantity)
            {
                var timeString = exportRelativeTime
                    ? ((sample.TimestampTicks - firstTimestampTicks.Value) / (double)TimeSpan.TicksPerSecond).ToString("F3")
                    : new DateTime(sample.TimestampTicks).ToString("O");

                sb.Clear();
                sb.Append(timeString);

                foreach (var channelName in channelNames)
                {
                    sb.Append(_delimiter);
                    if (tempCounts[channelName] > 0)
                    {
                        var average = tempTotals[channelName] / tempCounts[channelName];
                        sb.Append(average.ToString("G"));
                    }
                }
                sb.AppendLine();

                writer.Write(sb.ToString());

                // Reset accumulators
                foreach (var channelName in channelNames)
                {
                    tempTotals[channelName] = 0.0;
                    tempCounts[channelName] = 0;
                }
                count = 0;

                // Update progress periodically
                if (totalProcessed % 1000 == 0)
                {
                    var progressPercentage = Math.Min(100, (int)((double)totalProcessed / totalSamples * 100));
                    var overallProgress = (int)((sessionIndex + progressPercentage / 100.0) * (100.0 / totalSessions));
                    bw.ReportProgress(overallProgress);
                }
            }

            if (bw.CancellationPending)
                return;
        }
    }
}