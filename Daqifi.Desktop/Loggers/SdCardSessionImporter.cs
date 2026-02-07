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
        _logger.Information($"Starting file import from '{Path.GetFileName(filePath)}'");
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
        _logger.Information($"Starting stream import for '{fileName}'");
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

        _logger.Information($"Starting import of '{fileName}' from device {device.DeviceSerialNo}");

        // Download to temp file
        var downloadResult = await device.DownloadSdCardFileAsync(fileName, null, ct);

        if (string.IsNullOrEmpty(downloadResult.FilePath))
        {
            throw new InvalidOperationException($"Download completed but no local file path was returned for '{fileName}'.");
        }

        // Validate the downloaded file is in a temp directory
        var tempDir = Path.GetTempPath();
        var fullDownloadPath = Path.GetFullPath(downloadResult.FilePath);
        if (!fullDownloadPath.StartsWith(tempDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Downloaded file path is not in the expected temp directory.");
        }

        try
        {
            // Log download result for diagnostics
            var fileInfo = new FileInfo(downloadResult.FilePath);
            _logger.Information(
                $"Download complete: FileName='{downloadResult.FileName}', " +
                $"ReportedSize={downloadResult.FileSize}, " +
                $"DiskSize={fileInfo.Length}, " +
                $"TempPath='{downloadResult.FilePath}'");

            if (fileInfo.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Downloaded file '{fileName}' is empty (0 bytes). " +
                    "The SD card log file may not contain any data.");
            }

            // Parse using the original device filename (not the temp path)
            // so the parser can extract the date from the log filename pattern.
            // Pass the device's TimestampFrequency as fallback since firmware
            // may not include it in SD card log data.
            var parseOptions = new SdCardParseOptions
            {
                FallbackTimestampFrequency = device.TimestampFrequency
            };

            if (parseOptions.FallbackTimestampFrequency > 0)
            {
                _logger.Information(
                    $"Using device TimestampFrequency={parseOptions.FallbackTimestampFrequency} Hz as fallback for SD card parsing");
            }

            var parser = new SdCardFileParser();
            await using var fileStream = new FileStream(
                downloadResult.FilePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 65536, useAsync: true);
            var logSession = await parser.ParseAsync(
                fileStream, downloadResult.FileName, parseOptions, ct);
            var session = await ImportSessionAsync(logSession, options, progress, ct);

            // Optionally delete from device after successful import
            if (options.DeleteFromDeviceAfterImport)
            {
                _logger.Information($"Deleting '{fileName}' from device after successful import");
                await device.DeleteSdCardFileAsync(fileName, ct);
            }

            _logger.Information($"Successfully imported '{fileName}' from device {device.DeviceSerialNo}");
            return session;
        }
        finally
        {
            // Clean up temp file
            if (File.Exists(downloadResult.FilePath))
            {
                try
                {
                    File.Delete(downloadResult.FilePath);
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Failed to clean up temp file '{downloadResult.FilePath}': {ex.Message}");
                }
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

        _logger.Information(
            $"SD card session config: AnalogPorts={analogPortCount}, DigitalPorts={digitalPortCount}, " +
            $"Device={deviceSerialNo}, TimestampFreq={config?.TimestampFrequency ?? 0}");

        // Pre-assign colors per channel
        var channelColors = new Dictionary<string, string>();
        AssignChannelColors(channelColors, analogPortCount, digitalPortCount);

        // Create the logging session in the database
        var session = CreateSession(logSession, options);

        if ((config?.TimestampFrequency ?? 0) == 0)
        {
            _logger.Warning(
                "No TimestampFrequency found in SD card file. " +
                "Timestamps may not be properly reconstructed. " +
                "Device firmware may not include TimestampFreq in logged messages.");
        }

        // Bulk-insert samples
        var batch = new List<DataSample>();
        long samplesProcessed = 0;
        var sampleIndex = 0;

        await foreach (var entry in logSession.Samples.WithCancellation(ct))
        {
            // Log first and second sample for diagnostics (to verify timestamps are spaced correctly)
            if (sampleIndex < 2)
            {
                _logger.Information(
                    $"Sample[{sampleIndex}]: AnalogValues.Count={entry.AnalogValues.Count}, " +
                    $"DigitalData=0x{entry.DigitalData:X8}, Timestamp={entry.Timestamp:O}");
            }

            sampleIndex++;

            // If we didn't have config, discover channel count from first entry
            if (analogPortCount == 0 && entry.AnalogValues.Count > 0)
            {
                analogPortCount = entry.AnalogValues.Count;
                _logger.Information($"Discovered {analogPortCount} analog channels from first sample");
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

        if (samplesProcessed == 0)
        {
            _logger.Warning(
                $"No samples found in SD card file '{logSession.FileName}'. " +
                $"DeviceConfig present: {config != null}");
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
