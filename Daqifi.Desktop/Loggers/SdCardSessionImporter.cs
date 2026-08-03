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
    /// <exception cref="SdCardTransferStalledException">
    /// Thrown when the transfer stops making progress before the end-of-file marker arrives. Core
    /// raises it directly for a transport that goes quiet or closes; the desktop raises it for its
    /// own stall watchdog and for Core's hard download deadline (see
    /// <see cref="SdCardSessionImporter.DOWNLOAD_STALL_TIMEOUT"/>).
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
    ///
    /// Core v1.4.0 bounds the download itself (daqifi-core#399/#401), but its budget is 30 minutes
    /// and is not settable from here (<c>DaqifiStreamingDevice.SdCardDownloadTimeout</c> is
    /// <c>internal virtual</c>), so this shorter bound is kept rather than handing the UI a
    /// half-hour busy overlay.
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

        // Download to temp file, bounded by a stall watchdog tighter than Core's own 30-minute
        // download budget (see DOWNLOAD_STALL_TIMEOUT).
        var downloadResult = await DownloadWithStallWatchdogAsync(device, fileName, ct);

        if (string.IsNullOrEmpty(downloadResult.FilePath))
        {
            // A contract violation, not a device condition: Core's temp-file overload always sets
            // FilePath on the result it returns, and throws otherwise. Left as an unclassified
            // failure on purpose — it keeps the Error/Sentry path, which is where a broken
            // implementation of IStreamingDevice belongs. It is NOT the wedged-SD-subsystem
            // condition it used to be reported as: a device that opens a file and serves nothing is
            // Core's SdCardEmptyTransferException, raised from inside the download call above.
            throw new InvalidOperationException(
                $"The SD card download of '{fileName}' reported success without producing a local file.");
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
                // A legitimately empty log, not a failure. Core v1.4.0 discriminates the two using
                // the directory listing's reported size (daqifi-core#398 gap 2): a marker-only
                // transfer for a file the listing calls non-empty — or whose listed size is unknown
                // — still raises SdCardEmptyTransferException from inside the download call, so
                // reaching here means the listing itself said 0 bytes. An interrupted logging
                // session routinely leaves such a file on a FAT card; it imports as an empty
                // session rather than failing (and taking the rest of an Import All batch with it,
                // issue #780).
                _logger.Warning(
                    $"SD card file '{fileName}' is empty: the device listed it as 0 bytes and served no " +
                    "data. Importing it as an empty session.");
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
    /// <exception cref="SdCardTransferStalledException">
    /// Thrown when no data arrives for <see cref="_downloadStallTimeout"/>, or when Core abandons
    /// the download on its own hard deadline. Core's own stalls — a transport that returned an
    /// empty read or closed — already arrive as this type and pass through untouched. Callers
    /// surface all of them as expected device conditions rather than app errors.
    /// </exception>
    internal async Task<SdCardDownloadResult> DownloadWithStallWatchdogAsync(
        IStreamingDevice device,
        string fileName,
        CancellationToken ct)
    {
        using var stallCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, stallCts.Token);

        // How much of the file arrived before it stalled, so the stall report carries the same
        // diagnostic Core's own stalls do. Written from the progress callback and read from the
        // catch blocks, which can be different threads.
        long bytesReceived = 0;
        var elapsed = System.Diagnostics.Stopwatch.StartNew();

        stallCts.CancelAfter(_downloadStallTimeout);
        var transferProgress = new SynchronousProgress<SdCardTransferProgress>(report =>
        {
            // Another chunk arrived, so the device is alive — restart the deadline.
            Volatile.Write(ref bytesReceived, report.BytesReceived);

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
            // stall, so a genuine user cancel still propagates as OperationCanceledException.
            //
            // Reported as TransferTimeout rather than NoDataReceived because that is what it means
            // to the classifier: the deadline this importer imposes on a transfer elapsed with the
            // file incomplete. NoDataReceived is Core's ordinary per-read stall, which gives up in
            // well under a second and says far less about the device (see SdCardFailureClassifier).
            throw new SdCardTransferStalledException(
                fileName,
                Volatile.Read(ref bytesReceived),
                SdCardTransferStallReason.TransferTimeout,
                _downloadStallTimeout);
        }
        catch (TimeoutException ex)
        {
            // Core v1.4.0 types its transport stalls (daqifi-core#398 gap 1), but its own hard
            // download deadline still surfaces as a bare TimeoutException: the worker was abandoned
            // mid-transfer, possibly parked in native serial I/O, so there is no receiver left to
            // report a reason. Unnormalised it would fall through to the classifier's default arm —
            // a Sentry issue plus "check the device connection", which is the exact #779 regression
            // this catch exists to prevent.
            //
            // Caught by type rather than by message, and deliberately not tied to one Core throw
            // site. What makes the normalisation safe is the *scope*: a timeout out of the download
            // call is by definition a transfer that did not complete, whereas a TimeoutException
            // from anywhere else in the import says nothing about the SD card and must keep the
            // generic Error path. Core's exception is preserved as the inner one.
            //
            // Note that Core's own typed stalls do not come through here at all:
            // SdCardTransferStalledException derives from SdCardOperationException, not
            // TimeoutException, so it propagates to the classifier untouched.
            //
            // A caller-requested cancel wins over all of that. Over serial the read does not
            // observe the token, so pressing cancel can surface as a transport timeout rather than
            // a cancellation — and reporting that back as a device fault would tell the user their
            // hardware failed when in fact they stopped it themselves.
            ct.ThrowIfCancellationRequested();

            // The elapsed time, not the desktop's stall window: reaching Core's own deadline means
            // progress kept arriving inside every one of this importer's windows, so reporting the
            // window would misstate how long the user actually waited.
            throw new SdCardTransferStalledException(
                fileName,
                Volatile.Read(ref bytesReceived),
                SdCardTransferStallReason.TransferTimeout,
                elapsed.Elapsed,
                ex);
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
