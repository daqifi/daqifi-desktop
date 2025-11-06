using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Daqifi.Desktop.Services;

/// <summary>
/// Imports SD card logging sessions into the application database
/// </summary>
public class LoggingSessionImporter
{
    #region Private Fields
    private readonly AppLogger _logger = AppLogger.Instance;
    private readonly IDbContextFactory<LoggingContext> _loggingContext;
    private readonly SdCardLogParser _parser;
    #endregion

    #region Constructor
    public LoggingSessionImporter()
    {
        _loggingContext = App.ServiceProvider.GetRequiredService<IDbContextFactory<LoggingContext>>();
        _parser = new SdCardLogParser();
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// Imports an SD card log file into the application as a new logging session
    /// </summary>
    /// <param name="sdCardFile">The SD card file to import</param>
    /// <param name="binaryData">The binary data from the SD card file</param>
    /// <param name="device">The device that created the log</param>
    /// <param name="progress">Optional progress reporting callback</param>
    /// <returns>The created logging session</returns>
    public async Task<LoggingSession> ImportSdCardLogAsync(
        SdCardFile sdCardFile,
        byte[] binaryData,
        IStreamingDevice device,
        IProgress<ImportProgress>? progress = null)
    {
        if (sdCardFile == null)
        {
            throw new ArgumentNullException(nameof(sdCardFile));
        }

        if (binaryData == null || binaryData.Length == 0)
        {
            throw new ArgumentException("Binary data cannot be null or empty", nameof(binaryData));
        }

        if (device == null)
        {
            throw new ArgumentNullException(nameof(device));
        }

        _logger.Information($"Starting import of SD card file: {sdCardFile.FileName}");

        try
        {
            // Report progress
            progress?.Report(new ImportProgress
            {
                Stage = "Parsing",
                PercentComplete = 0,
                Message = "Parsing log file..."
            });

            // Parse the binary data
            _parser.Reset();
            var samples = await Task.Run(() => _parser.ParseLogFile(binaryData, device));

            if (samples.Count == 0)
            {
                throw new InvalidOperationException("No data samples found in the log file");
            }

            _logger.Information($"Parsed {samples.Count} samples from SD card file");

            // Report progress
            progress?.Report(new ImportProgress
            {
                Stage = "Creating Session",
                PercentComplete = 50,
                Message = "Creating logging session..."
            });

            // Create a new logging session
            using var context = _loggingContext.CreateDbContext();

            var ids = await context.Sessions.AsNoTracking().Select(s => s.ID).ToListAsync();
            var newId = ids.Count > 0 ? ids.Max() + 1 : 0;

            var session = new LoggingSession(newId, $"Imported_{sdCardFile.FileName}")
            {
                SessionStart = sdCardFile.CreatedDate != DateTime.MinValue
                    ? sdCardFile.CreatedDate
                    : DateTime.Now
            };

            // Set the logging session ID for all samples
            foreach (var sample in samples)
            {
                sample.LoggingSessionID = session.ID;
            }

            // Save to database
            progress?.Report(new ImportProgress
            {
                Stage = "Saving",
                PercentComplete = 75,
                Message = "Saving to database..."
            });

            context.Sessions.Add(session);
            await context.SaveChangesAsync();

            // Save samples in batches for better performance
            const int batchSize = 1000;
            for (var i = 0; i < samples.Count; i += batchSize)
            {
                var batch = samples.Skip(i).Take(batchSize).ToList();
                context.Samples.AddRange(batch);
                await context.SaveChangesAsync();

                // Report batch progress
                var batchProgress = 75 + (int)((i / (double)samples.Count) * 25);
                progress?.Report(new ImportProgress
                {
                    Stage = "Saving",
                    PercentComplete = batchProgress,
                    Message = $"Saving samples... ({i + batch.Count}/{samples.Count})"
                });
            }

            // Report completion
            progress?.Report(new ImportProgress
            {
                Stage = "Complete",
                PercentComplete = 100,
                Message = "Import complete!"
            });

            _logger.Information($"Successfully imported {samples.Count} samples into session {session.ID}");

            return session;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Failed to import SD card file: {sdCardFile.FileName}");
            throw;
        }
    }
    #endregion
}

/// <summary>
/// Represents the progress of an import operation
/// </summary>
public class ImportProgress
{
    public string Stage { get; set; } = string.Empty;
    public int PercentComplete { get; set; }
    public string Message { get; set; } = string.Empty;
}
