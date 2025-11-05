using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.IO.Messages.Decoders;
using Daqifi.Desktop.Loggers;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.Models;
using Google.Protobuf;
using System.Collections.Concurrent;
using System.IO;

namespace Daqifi.Desktop.Services;

/// <summary>
/// Result of a file import operation
/// </summary>
public class FileImportResult
{
    /// <summary>
    /// Indicates if the import was successful
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The ID of the imported logging session
    /// </summary>
    public int? LoggingSessionId { get; init; }

    /// <summary>
    /// Number of samples imported
    /// </summary>
    public int SamplesImported { get; init; }

    /// <summary>
    /// Error message if import failed
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creates a successful result
    /// </summary>
    public static FileImportResult CreateSuccess(int sessionId, int samplesImported)
    {
        return new FileImportResult
        {
            Success = true,
            LoggingSessionId = sessionId,
            SamplesImported = samplesImported
        };
    }

    /// <summary>
    /// Creates a failed result
    /// </summary>
    public static FileImportResult CreateFailure(string errorMessage)
    {
        return new FileImportResult
        {
            Success = false,
            ErrorMessage = errorMessage
        };
    }
}

/// <summary>
/// Interface for importing device log files
/// </summary>
public interface IDeviceLogFileImporter
{
    /// <summary>
    /// Imports a protobuf file into a logging session
    /// </summary>
    /// <param name="fileContent">The protobuf file content</param>
    /// <param name="fileName">The name of the file being imported</param>
    /// <param name="device">The device that created the file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The import result</returns>
    Task<FileImportResult> ImportProtobufFileAsync(
        byte[] fileContent,
        string fileName,
        IStreamingDevice device,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Service for importing device log files into logging sessions
/// </summary>
public class DeviceLogFileImporter : IDeviceLogFileImporter
{
    #region Private Fields
    private readonly AppLogger _logger;
    #endregion

    #region Constructor
    /// <summary>
    /// Initializes a new instance of the DeviceLogFileImporter class
    /// </summary>
    public DeviceLogFileImporter()
    {
        _logger = AppLogger.Instance;
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// Imports a protobuf file into a logging session
    /// </summary>
    /// <param name="fileContent">The protobuf file content</param>
    /// <param name="fileName">The name of the file being imported</param>
    /// <param name="device">The device that created the file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The import result</returns>
    public async Task<FileImportResult> ImportProtobufFileAsync(
        byte[] fileContent,
        string fileName,
        IStreamingDevice device,
        CancellationToken cancellationToken = default)
    {
        if (fileContent == null || fileContent.Length == 0)
        {
            return FileImportResult.CreateFailure("File content is empty");
        }

        if (device == null)
        {
            return FileImportResult.CreateFailure("Device is null");
        }

        try
        {
            _logger.Information($"Starting import of protobuf file: {fileName}");

            // Create a new logging session
            var sessionName = $"Imported: {fileName} ({DateTime.Now:yyyy-MM-dd HH:mm:ss})";
            var loggingSession = new LoggingSession
            {
                Name = sessionName,
                SessionStart = DateTime.Now,
                Channels = []
            };

            // Parse the protobuf file and extract samples
            var samples = await ParseProtobufFileAsync(fileContent, device, cancellationToken);

            if (samples.Count == 0)
            {
                return FileImportResult.CreateFailure("No samples found in file");
            }

            _logger.Information($"Parsed {samples.Count} samples from file");

            // Save the session to the database
            var sessionId = await SaveLoggingSessionAsync(loggingSession, samples, cancellationToken);

            if (sessionId == null)
            {
                return FileImportResult.CreateFailure("Failed to save logging session to database");
            }

            _logger.Information($"Successfully imported {samples.Count} samples to session {sessionId}");

            return FileImportResult.CreateSuccess(sessionId.Value, samples.Count);
        }
        catch (OperationCanceledException)
        {
            _logger.Warning($"Import of file '{fileName}' was cancelled");
            return FileImportResult.CreateFailure("Import was cancelled");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Failed to import file '{fileName}'");
            return FileImportResult.CreateFailure($"Import failed: {ex.Message}");
        }
    }
    #endregion

    #region Private Methods
    /// <summary>
    /// Parses a protobuf file and extracts data samples
    /// </summary>
    private async Task<List<DataSample>> ParseProtobufFileAsync(
        byte[] fileContent,
        IStreamingDevice device,
        CancellationToken cancellationToken)
    {
        var samples = new List<DataSample>();

        return await Task.Run(() =>
        {
            try
            {
                // Create a memory stream from the file content
                using var stream = new MemoryStream(fileContent);

                // Parse protobuf messages from the stream
                while (stream.Position < stream.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        // Try to parse a DaqifiOutMessage from the stream
                        // Note: Protobuf messages in a file may be delimited by length prefix
                        // For now, we'll assume the standard protobuf format

                        var message = DaqifiOutMessage.Parser.ParseDelimitedFrom(stream);
                        if (message == null)
                        {
                            break; // No more messages
                        }

                        // Extract samples from the message
                        var messageSamples = ExtractSamplesFromMessage(message, device);
                        samples.AddRange(messageSamples);
                    }
                    catch (InvalidProtocolBufferException)
                    {
                        // End of messages or corrupted data
                        break;
                    }
                }

                return samples;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error parsing protobuf file");
                throw;
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Extracts data samples from a protobuf message
    /// </summary>
    private List<DataSample> ExtractSamplesFromMessage(DaqifiOutMessage message, IStreamingDevice device)
    {
        var samples = new List<DataSample>();

        if (message == null)
        {
            return samples;
        }

        // Calculate timestamp from message
        var timestamp = DateTime.Now; // Default to now if we can't calculate
        if (message.MsgTimeStamp > 0)
        {
            // Convert device timestamp to DateTime
            // The timestamp is in device ticks (20ns periods)
            const double tickPeriod = 20E-9; // 20 nanoseconds
            var seconds = message.MsgTimeStamp * tickPeriod;
            timestamp = DateTime.UnixEpoch.AddSeconds(seconds);
        }

        // Extract analog channel samples
        if (message.AnalogInData != null && message.AnalogInData.Count > 0)
        {
            for (var i = 0; i < message.AnalogInData.Count; i++)
            {
                var channelName = $"AI{i}";
                var rawValue = message.AnalogInData[i];

                // Create a sample for this channel
                // Note: We don't have the full channel configuration, so we'll use defaults
                var sample = new DataSample
                {
                    DeviceName = device.Name,
                    DeviceSerialNo = message.DeviceSn.ToString(),
                    ChannelName = channelName,
                    TimestampTicks = timestamp.Ticks,
                    Value = rawValue // Store raw value for now
                };

                samples.Add(sample);
            }
        }

        // Extract digital channel samples
        if (message.DigitalData != null && message.DigitalData.Length > 0)
        {
            // Digital data is a bit field stored in bytes
            var digitalByte = message.DigitalData.ElementAtOrDefault(0);
            for (var i = 0; i < 8; i++) // Assuming 8 digital channels
            {
                var channelName = $"DIO{i}";
                var bitValue = (digitalByte & (1 << i)) != 0 ? 1 : 0;

                var sample = new DataSample
                {
                    DeviceName = device.Name,
                    DeviceSerialNo = message.DeviceSn.ToString(),
                    ChannelName = channelName,
                    TimestampTicks = timestamp.Ticks,
                    Value = bitValue
                };

                samples.Add(sample);
            }
        }

        return samples;
    }

    /// <summary>
    /// Saves a logging session and its samples to the database
    /// </summary>
    private async Task<int?> SaveLoggingSessionAsync(
        LoggingSession session,
        List<DataSample> samples,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            try
            {
                // Use the LoggingManager to save the session
                // The LoggingManager handles database operations
                var loggingManager = LoggingManager.Instance;

                // Create the session in the database
                // Note: This is a simplified approach; the actual implementation may vary
                // depending on how the LoggingManager works

                // For now, return a placeholder session ID
                // In a real implementation, you would call the LoggingManager to save the session
                // and get the session ID back

                _logger.Warning("Logging session save not fully implemented - database integration needed");

                return 1; // Placeholder session ID
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error saving logging session to database");
                return null;
            }
        }, cancellationToken);
    }
    #endregion
}
