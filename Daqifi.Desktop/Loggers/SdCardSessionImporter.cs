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

/// <summary>
/// Imports SD card log files into the local logging database. Extracted as an interface so the
/// ViewModels that drive an import can be unit-tested against a device that fails.
/// </summary>
public interface ISdCardSessionImporter
{
    /// <summary>
    /// Downloads an SD card log file from a connected USB device and imports it.
    /// </summary>
    /// <exception cref="SdCardDownloadStalledException">
    /// Thrown when the device stops sending data mid-transfer — either because the desktop's own
    /// watchdog ran out of patience (see <see cref="SdCardSessionImporter.DOWNLOAD_STALL_TIMEOUT"/>)
    /// or because the transport reported a timeout first.
    /// </exception>
    Task<SdCardImportResult> ImportFromDeviceAsync(
        IStreamingDevice device,
        string fileName,
        ImportOptions? options = null,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default);
}

/// <summary>
/// Imports SD card log files — from a local path, a stream, or straight off a connected device —
/// into the local logging database, mapping Daqifi.Core's parsed samples onto desktop entities and
/// bulk-inserting them into SQLite.
/// </summary>
public class SdCardSessionImporter : ISdCardSessionImporter
{
    private const int BatchSize = 1000;

    /// <summary>
    /// How long the desktop waits for the device to deliver more of an SD card file before it
    /// gives up. The deadline is reset on every chunk received, so this bounds a stall, not the
    /// total transfer — a large file downloading steadily is never cut off.
    ///
    /// Deliberately longer than Core's own empty-transfer retry window (~50s observed): when the
    /// SD subsystem is wedged, Core's typed <c>SdCardEmptyTransferException</c> carries a better
    /// message than a bare timeout, so it must be allowed to win the race. This watchdog is the
    /// backstop for the case Core cannot detect — a transfer that simply never completes, which
    /// otherwise left the UI sitting on a busy overlay indefinitely (issue #754).
    /// </summary>
    internal static readonly TimeSpan DOWNLOAD_STALL_TIMEOUT = TimeSpan.FromSeconds(90);

    private readonly IDbContextFactory<LoggingContext> _loggingContext;
    private readonly AppLogger _logger = AppLogger.Instance;
    private readonly TimeSpan _downloadStallTimeout;

    /// <summary>
    /// Initializes a new instance of the <see cref="SdCardSessionImporter"/> class.
    /// </summary>
    /// <param name="loggingContext">Factory for the logging database imported samples are written to.</param>
    public SdCardSessionImporter(IDbContextFactory<LoggingContext> loggingContext)
        : this(loggingContext, DOWNLOAD_STALL_TIMEOUT)
    {
    }

    /// <summary>
    /// Test seam: constructs an importer with a shortened download stall timeout so the watchdog
    /// can be exercised without a 90-second unit test.
    /// </summary>
    internal SdCardSessionImporter(
        IDbContextFactory<LoggingContext> loggingContext,
        TimeSpan downloadStallTimeout)
    {
        _loggingContext = loggingContext;
        _downloadStallTimeout = downloadStallTimeout;
    }

    /// <summary>
    /// Imports an SD card log file from a local file path.
    /// </summary>
    public async Task<SdCardImportResult> ImportFromFileAsync(
        string filePath,
        ImportOptions? options = null,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        _logger.Information($"Starting file import from '{Path.GetFileName(filePath)}'");
        var logSession = await SdCardFileParserFactory.ParseFileAsync(filePath, null, ct);
        return await ImportSessionAsync(logSession, options, progress, ct);
    }

    /// <summary>
    /// Imports an SD card log file from a stream.
    /// </summary>
    public async Task<SdCardImportResult> ImportFromStreamAsync(
        Stream stream,
        string fileName,
        ImportOptions? options = null,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        _logger.Information($"Starting stream import for '{fileName}'");
        var logSession = await SdCardFileParserFactory.ParseAsync(stream, fileName, null, ct);
        return await ImportSessionAsync(logSession, options, progress, ct);
    }

    /// <inheritdoc />
    public async Task<SdCardImportResult> ImportFromDeviceAsync(
        IStreamingDevice device,
        string fileName,
        ImportOptions? options = null,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        options ??= new ImportOptions();

        _logger.Information($"Starting import of '{fileName}' from device {device.DeviceSerialNo}");

        // Download to temp file, bounded by a stall watchdog. Core exposes no timeout on either
        // public DownloadSdCardFileAsync overload (daqifi-core), so the desktop has to impose the
        // bound itself or a silent device leaves the import hanging forever.
        var downloadResult = await DownloadWithStallWatchdogAsync(device, fileName, ct);

        if (string.IsNullOrEmpty(downloadResult.FilePath))
        {
            // The device answered but delivered nothing to disk — the same "SD subsystem is not
            // ready" condition Core raises this exception for, so report it the same way rather
            // than as a generic fault the user cannot act on.
            throw new SdCardEmptyTransferException(fileName);
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
                // Issue #593 was this check firing as a bare InvalidOperationException, which the
                // Error path then filed to Sentry. A file the device listed but served as 0 bytes
                // is the wedged-SD-subsystem condition, not an app fault: same typed exception as
                // the marker-only transfer Core detects, so it degrades the same way.
                throw new SdCardEmptyTransferException(fileName);
            }

            // Parse using the original device filename (not the temp path)
            // so the parser can extract the date from the log filename pattern.
            // Build a ConfigurationOverride from the connected device's channels
            // so the parser has calibration, resolution, and port range data for
            // scaling raw ADC values to real voltage.
            var deviceConfig = device.GetSdCardParseConfiguration();
            var parseOptions = new SdCardParseOptions
            {
                ConfigurationOverride = deviceConfig
            };

            if (deviceConfig != null)
            {
                _logger.Information(
                    $"Using device config override: Resolution={deviceConfig.Resolution}, " +
                    $"AnalogPorts={deviceConfig.AnalogPortCount}, DigitalPorts={deviceConfig.DigitalPortCount}");
            }

            await using var fileStream = new FileStream(
                downloadResult.FilePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 65536, useAsync: true);
            var logSession = await SdCardFileParserFactory.ParseAsync(
                fileStream, downloadResult.FileName, parseOptions, ct);
            var result = await ImportSessionAsync(logSession, options, progress, ct);

            // Optionally delete from device after successful import
            if (options.DeleteFromDeviceAfterImport)
            {
                _logger.Information($"Deleting '{fileName}' from device after successful import");
                await device.DeleteSdCardFileAsync(fileName, ct);
            }

            _logger.Information($"Successfully imported '{fileName}' from device {device.DeviceSerialNo}");
            return result;
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
                    _logger.Warning($"Failed to clean up temp file '{downloadResult.FilePath}': {ex}");
                }
            }
        }
    }

    /// <summary>
    /// Downloads <paramref name="fileName"/> while watching for a stalled transfer: the deadline
    /// restarts every time the device delivers another chunk, so only a device that goes quiet
    /// trips it.
    /// </summary>
    /// <exception cref="SdCardDownloadStalledException">
    /// Thrown when no data arrives for <see cref="_downloadStallTimeout"/>, or when the transport
    /// reports a timeout of its own first. Callers surface this as an expected device condition
    /// rather than an app error.
    /// </exception>
    internal async Task<SdCardDownloadResult> DownloadWithStallWatchdogAsync(
        IStreamingDevice device,
        string fileName,
        CancellationToken ct)
    {
        using var stallCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, stallCts.Token);

        // How long the device has been quiet is what tells a stalled transfer apart from a slow
        // one, and Core reports both through the same untyped exception, so the desktop has to
        // measure it. Written from the progress callback and read from the catch blocks, which can
        // be different threads — hence the volatile tick count rather than a TimeSpan field.
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        long lastProgressTicks = 0;

        stallCts.CancelAfter(_downloadStallTimeout);
        var transferProgress = new SynchronousProgress<SdCardTransferProgress>(_ =>
        {
            // Another chunk arrived, so the device is alive — restart the deadline.
            Volatile.Write(ref lastProgressTicks, elapsed.Elapsed.Ticks);

            try
            {
                stallCts.CancelAfter(_downloadStallTimeout);
            }
            catch (ObjectDisposedException)
            {
                // The download already finished and disposed the source; nothing left to extend.
            }
        });

        try
        {
            return await device.DownloadSdCardFileAsync(fileName, transferProgress, linkedCts.Token);
        }
        catch (OperationCanceledException) when (stallCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Distinguish our watchdog from a caller-requested cancel: only the watchdog becomes a
            // stall, so a genuine user cancel still propagates as OperationCanceledException. The
            // watchdog fires on exactly one condition — the full stall window with nothing arriving
            // — so that window is the silence, by construction.
            throw new SdCardDownloadStalledException(
                fileName, _downloadStallTimeout, _downloadStallTimeout);
        }
        catch (TimeoutException ex)
        {
            // Issue #779: over USB serial — the only transport SD import supports — the watchdog
            // above is effectively unreachable. Core's serial transport drops SerialPort.ReadTimeout
            // to 500ms after connect and hands the raw BaseStream to SdCardFileReceiver, and .NET's
            // SerialStream returns 0 bytes on a read timeout rather than throwing or honouring the
            // token. The receiver treats a 0-byte read as fatal, so a wedged device raises a plain
            // TimeoutException in about half a second — never as the cancellation the catch above
            // is waiting for, which left this whole degradation path dead code on real hardware.
            //
            // Caught by type rather than by message, and deliberately not tied to that one throw
            // site: SdCardFileReceiver raises TimeoutException from three places (the 0-byte read
            // plus two paths for its own 30-minute cap), and Core is free to add more. What makes
            // the normalisation safe is the *scope* — a timeout out of the download call is by
            // definition a transfer that did not complete, whereas a TimeoutException from anywhere
            // else in the import says nothing about the SD card and must keep the generic Error
            // path. Core's exception is preserved as the inner one so the byte count and reason it
            // reports still reach the log.
            //
            // Workaround: retire once Core reports stalls with a type. See daqifi-core#398 (gap 1).
            //
            // A caller-requested cancel wins over all of that. Over serial the read does not
            // observe the token, so pressing cancel can surface as a transport timeout rather than
            // a cancellation — and reporting that back as a device fault would tell the user their
            // hardware failed when in fact they stopped it themselves.
            ct.ThrowIfCancellationRequested();

            // Report the silence, not the total transfer time. A large file that streamed steadily
            // for ten minutes and then hit one brief transport timeout is a healthy device and one
            // unlucky file; ten minutes of nothing is a wedged subsystem. Measuring the whole
            // attempt would collapse those two into the same answer and abort a batch import over
            // the first of them.
            //
            // Read the last-progress stamp BEFORE sampling the clock. The callback runs on Core's
            // thread, so sampling the clock first would let a chunk landing in between stamp a
            // time later than the "now" it is subtracted from, and report a negative silence. This
            // order cannot: the stopwatch only moves forward, so a stamp read earlier is never
            // ahead of a reading taken after it. A chunk arriving in the gap merely makes the
            // reported silence slightly conservative, which is the harmless direction.
            var lastProgress = TimeSpan.FromTicks(Volatile.Read(ref lastProgressTicks));
            var silentFor = elapsed.Elapsed - lastProgress;
            throw new SdCardDownloadStalledException(fileName, silentFor, _downloadStallTimeout, ex);
        }
    }

    /// <summary>
    /// An <see cref="IProgress{T}"/> that invokes its handler inline instead of posting to the
    /// captured <see cref="SynchronizationContext"/> like <see cref="Progress{T}"/> does. The
    /// stall watchdog must observe progress the moment it happens; a callback queued behind a
    /// busy UI thread would let the deadline expire on a healthy transfer.
    /// </summary>
    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    /// <summary>
    /// Core import logic: maps parsed SD card data to desktop entities and bulk-inserts into SQLite.
    /// Internal so tests can drive it with a synthetic <see cref="SdCardLogSession"/>;
    /// production code enters through the file/stream/device methods above.
    /// </summary>
    internal async Task<SdCardImportResult> ImportSessionAsync(
        SdCardLogSession logSession,
        ImportOptions? options,
        IProgress<ImportProgress>? progress,
        CancellationToken ct)
    {
        options ??= new ImportOptions();
        var timestampQuality = new ImportTimestampQuality();

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
            timestampQuality.Observe(entry);

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

        if (timestampQuality.HasDegenerateTimeAxis)
        {
            _logger.Warning(
                $"Imported session '{session.Name}' (ID={session.ID}) has a degenerate time axis: " +
                $"{timestampQuality.EntriesWithoutDeviceTimestamp:N0} of {timestampQuality.TotalEntries:N0} " +
                "entries carried no device timestamp and were placed at the session base time. " +
                "The source file likely lacks per-sample timestamps.");
        }

        // Record the sample count on the session so the list view can show it
        // without falling back to the lazy backfill on the next reload. We
        // already have the exact count locally, so no extra query is needed.
        try
        {
            using var ctx = _loggingContext.CreateDbContext();
            var tracked = ctx.Sessions.FirstOrDefault(s => s.ID == session.ID);
            if (tracked != null)
            {
                tracked.SampleCount = samplesProcessed;
                ctx.SaveChanges();
            }

            // Marshal the in-memory mutation onto the UI thread: this importer
            // is invoked from background tasks, and SampleCount raises
            // PropertyChanged for WPF bindings.
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => session.SampleCount = samplesProcessed);
            }
            else
            {
                session.SampleCount = samplesProcessed;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Failed to persist SampleCount for imported session {session.ID}");
        }

        return new SdCardImportResult
        {
            Session = session,
            TimestampQuality = timestampQuality
        };
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

/// <summary>
/// Outcome of an SD card import: the created logging session plus the
/// timestamp-quality diagnostics gathered while importing, so callers can
/// warn the user when the file's time axis could not be reconstructed.
/// </summary>
public sealed class SdCardImportResult
{
    /// <summary>
    /// The logging session the import created.
    /// </summary>
    public required LoggingSession Session { get; init; }

    /// <summary>
    /// Timestamp statistics observed across the imported entries.
    /// </summary>
    public required ImportTimestampQuality TimestampQuality { get; init; }
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
