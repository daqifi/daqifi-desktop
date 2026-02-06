using System.Globalization;
using System.IO;
using Daqifi.Core.Device.SdCard;
using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Logger;
using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using ChannelType = Daqifi.Core.Channel.ChannelType;

namespace Daqifi.Desktop.Loggers;

public class SdCardSessionImporter
{
    private const int BatchSize = 1000;
    private readonly IDbContextFactory<LoggingContext> _loggingContext;
    private readonly AppLogger _logger = AppLogger.Instance;

    public SdCardSessionImporter(IDbContextFactory<LoggingContext> loggingContext)
    {
        _loggingContext = loggingContext;
    }

    /// <summary>
    /// Imports an SD card log file from a local file path.
    /// </summary>
    public async Task<LoggingSession> ImportFromFileAsync(
        string filePath,
        ImportOptions? options = null,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        var parser = new SdCardFileParser();
        var logSession = await parser.ParseFileAsync(filePath, null, ct);
        return await ImportSessionAsync(logSession, options, progress, ct);
    }

    /// <summary>
    /// Imports an SD card log file from a stream.
    /// </summary>
    public async Task<LoggingSession> ImportFromStreamAsync(
        Stream stream,
        string fileName,
        ImportOptions? options = null,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        var parser = new SdCardFileParser();
        var logSession = await parser.ParseAsync(stream, fileName, null, ct);
        return await ImportSessionAsync(logSession, options, progress, ct);
    }

    /// <summary>
    /// Downloads an SD card log file from a connected USB device and imports it.
    /// </summary>
    public async Task<LoggingSession> ImportFromDeviceAsync(
        IStreamingDevice device,
        string fileName,
        ImportOptions? options = null,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        options ??= new ImportOptions();

        // Download to temp file
        var downloadResult = await device.DownloadSdCardFileAsync(fileName, null, ct);

        try
        {
            // Parse and import
            var parser = new SdCardFileParser();
            var logSession = await parser.ParseFileAsync(downloadResult.FilePath!, null, ct);
            var session = await ImportSessionAsync(logSession, options, progress, ct);

            // Optionally delete from device after successful import
            if (options.DeleteFromDeviceAfterImport)
            {
                await device.DeleteSdCardFileAsync(fileName, ct);
            }

            return session;
        }
        finally
        {
            // Clean up temp file
            if (downloadResult.FilePath != null && File.Exists(downloadResult.FilePath))
            {
                try { File.Delete(downloadResult.FilePath); }
                catch { /* best effort cleanup */ }
            }
        }
    }

    /// <summary>
    /// Core import logic: maps parsed SD card data to desktop entities and bulk-inserts into SQLite.
    /// </summary>
    private async Task<LoggingSession> ImportSessionAsync(
        SdCardLogSession logSession,
        ImportOptions? options,
        IProgress<ImportProgress>? progress,
        CancellationToken ct)
    {
        options ??= new ImportOptions();

        var config = logSession.DeviceConfig;
        var deviceSerialNo = config?.DeviceSerialNumber ?? "Unknown";
        var deviceName = config?.DevicePartNumber
                         ?? Path.GetFileNameWithoutExtension(logSession.FileName);

        // Determine channel counts from config or discover from first sample
        var analogPortCount = config?.AnalogPortCount ?? 0;
        var digitalPortCount = config?.DigitalPortCount ?? 0;

        // Pre-assign colors per channel
        var channelColors = new Dictionary<string, string>();
        AssignChannelColors(channelColors, analogPortCount, digitalPortCount);

        // Create the logging session in the database
        var session = CreateSession(logSession, options);

        // Bulk-insert samples
        var batch = new List<DataSample>();
        long samplesProcessed = 0;

        await foreach (var entry in logSession.Samples.WithCancellation(ct))
        {
            // If we didn't have config, discover channel count from first entry
            if (analogPortCount == 0 && entry.AnalogValues.Count > 0)
            {
                analogPortCount = entry.AnalogValues.Count;
                AssignChannelColors(channelColors, analogPortCount, digitalPortCount);
            }

            // Create analog samples
            for (var i = 0; i < entry.AnalogValues.Count; i++)
            {
                var channelName = $"AI{i}";
                batch.Add(new DataSample
                {
                    LoggingSessionID = session.ID,
                    ChannelName = channelName,
                    DeviceName = deviceName,
                    DeviceSerialNo = deviceSerialNo,
                    Color = channelColors.GetValueOrDefault(channelName, "#D32F2F"),
                    Type = ChannelType.Analog,
                    Value = entry.AnalogValues[i],
                    TimestampTicks = entry.Timestamp.Ticks
                });
            }

            // Create digital samples (one per bit)
            for (var i = 0; i < digitalPortCount; i++)
            {
                var channelName = $"DI{i}";
                var bitValue = (entry.DigitalData & (1u << i)) != 0 ? 1.0 : 0.0;
                batch.Add(new DataSample
                {
                    LoggingSessionID = session.ID,
                    ChannelName = channelName,
                    DeviceName = deviceName,
                    DeviceSerialNo = deviceSerialNo,
                    Color = channelColors.GetValueOrDefault(channelName, "#757575"),
                    Type = ChannelType.Digital,
                    Value = bitValue,
                    TimestampTicks = entry.Timestamp.Ticks
                });
            }

            // Flush batch when full
            if (batch.Count >= BatchSize)
            {
                await FlushBatchAsync(batch, ct);
                samplesProcessed += batch.Count;
                batch.Clear();
                progress?.Report(new ImportProgress(samplesProcessed, null));
            }
        }

        // Flush remaining samples
        if (batch.Count > 0)
        {
            await FlushBatchAsync(batch, ct);
            samplesProcessed += batch.Count;
            batch.Clear();
            progress?.Report(new ImportProgress(samplesProcessed, null));
        }

        _logger.Information($"Imported {samplesProcessed} samples for session '{session.Name}' (ID={session.ID})");
        return session;
    }

    private LoggingSession CreateSession(SdCardLogSession logSession, ImportOptions options)
    {
        using var context = _loggingContext.CreateDbContext();

        var sessionName = options.SessionNameOverride
                          ?? $"SD Import - {Path.GetFileNameWithoutExtension(logSession.FileName)}";

        // Check for existing session with same name
        if (options.OverwriteExistingSession)
        {
            var existing = context.Sessions.FirstOrDefault(s => s.Name == sessionName);
            if (existing != null)
            {
                context.Sessions.Remove(existing);
                context.SaveChanges();
            }
        }

        // Generate new session ID (same pattern as LoggingManager.OnActiveChanged)
        var ids = context.Sessions.AsNoTracking().Select(s => s.ID).ToList();
        var newId = ids.Count > 0 ? ids.Max() + 1 : 0;

        var session = new LoggingSession(newId, sessionName)
        {
            SessionStart = logSession.FileCreatedDate ?? DateTime.Now
        };

        context.Sessions.Add(session);
        context.SaveChanges();

        return session;
    }

    private async Task FlushBatchAsync(List<DataSample> batch, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using var context = _loggingContext.CreateDbContext();
        using var transaction = context.Database.BeginTransaction();
        context.BulkInsert(batch);
        transaction.Commit();
    }

    private static void AssignChannelColors(
        Dictionary<string, string> channelColors,
        int analogPortCount,
        int digitalPortCount)
    {
        for (var i = 0; i < analogPortCount; i++)
        {
            var name = $"AI{i}";
            if (!channelColors.ContainsKey(name))
            {
                channelColors[name] = ChannelColorManager.Instance.NewColor()
                    .ToString(CultureInfo.InvariantCulture);
            }
        }

        for (var i = 0; i < digitalPortCount; i++)
        {
            var name = $"DI{i}";
            if (!channelColors.ContainsKey(name))
            {
                channelColors[name] = ChannelColorManager.Instance.NewColor()
                    .ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}

public class ImportOptions
{
    public bool DeleteFromDeviceAfterImport { get; set; }
    public bool OverwriteExistingSession { get; set; }
    public string? SessionNameOverride { get; set; }
}

public class ImportProgress
{
    public long SamplesProcessed { get; }
    public long? EstimatedTotal { get; }

    public double PercentComplete => EstimatedTotal is > 0
        ? (double)SamplesProcessed / EstimatedTotal.Value * 100
        : -1;

    public ImportProgress(long samplesProcessed, long? estimatedTotal)
    {
        SamplesProcessed = samplesProcessed;
        EstimatedTotal = estimatedTotal;
    }
}
