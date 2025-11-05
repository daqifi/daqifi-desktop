using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Models;
using System.IO;

namespace Daqifi.Desktop.Services;

/// <summary>
/// Result of a file download operation
/// </summary>
public class FileDownloadResult
{
    /// <summary>
    /// Indicates if the download was successful
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The downloaded file content
    /// </summary>
    public byte[]? Content { get; init; }

    /// <summary>
    /// Error message if download failed
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// The detected file type
    /// </summary>
    public SdCardFileType FileType { get; init; }

    /// <summary>
    /// Creates a successful result
    /// </summary>
    public static FileDownloadResult CreateSuccess(byte[] content, SdCardFileType fileType)
    {
        return new FileDownloadResult
        {
            Success = true,
            Content = content,
            FileType = fileType
        };
    }

    /// <summary>
    /// Creates a failed result
    /// </summary>
    public static FileDownloadResult CreateFailure(string errorMessage)
    {
        return new FileDownloadResult
        {
            Success = false,
            ErrorMessage = errorMessage,
            FileType = SdCardFileType.Unknown
        };
    }
}

/// <summary>
/// Interface for SD card file operations
/// </summary>
public interface ISdCardFileService
{
    /// <summary>
    /// Downloads a file from the device SD card
    /// </summary>
    /// <param name="device">The device to download from</param>
    /// <param name="fileName">The name of the file to download</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The download result</returns>
    Task<FileDownloadResult> DownloadFileAsync(
        IStreamingDevice device,
        string fileName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves downloaded file content to disk
    /// </summary>
    /// <param name="filePath">The path where to save the file</param>
    /// <param name="content">The file content</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if successful, false otherwise</returns>
    Task<bool> SaveToFileAsync(
        string filePath,
        byte[] content,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Service for SD card file operations
/// </summary>
public class SdCardFileService : ISdCardFileService
{
    #region Private Fields
    private readonly IFileTypeDetector _fileTypeDetector;
    private readonly AppLogger _logger;
    #endregion

    #region Constructor
    /// <summary>
    /// Initializes a new instance of the SdCardFileService class
    /// </summary>
    public SdCardFileService(IFileTypeDetector fileTypeDetector)
    {
        _fileTypeDetector = fileTypeDetector ?? throw new ArgumentNullException(nameof(fileTypeDetector));
        _logger = AppLogger.Instance;
    }

    /// <summary>
    /// Initializes a new instance with default dependencies
    /// </summary>
    public SdCardFileService() : this(new FileTypeDetector())
    {
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// Downloads a file from the device SD card
    /// </summary>
    /// <param name="device">The device to download from</param>
    /// <param name="fileName">The name of the file to download</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The download result</returns>
    public async Task<FileDownloadResult> DownloadFileAsync(
        IStreamingDevice device,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        if (device == null)
        {
            return FileDownloadResult.CreateFailure("Device is null");
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return FileDownloadResult.CreateFailure("File name is empty");
        }

        try
        {
            _logger.Information($"Starting download of file '{fileName}' from device '{device.Name}'");

            // Download the file content from the device
            var content = await DownloadFileContentAsync(device, fileName, cancellationToken);

            if (content == null || content.Length == 0)
            {
                return FileDownloadResult.CreateFailure("Downloaded content is empty");
            }

            // Detect file type
            var fileType = _fileTypeDetector.DetectFileType(fileName, content);

            _logger.Information($"Successfully downloaded file '{fileName}' ({content.Length} bytes, type: {fileType})");

            return FileDownloadResult.CreateSuccess(content, fileType);
        }
        catch (OperationCanceledException)
        {
            _logger.Warning($"Download of file '{fileName}' was cancelled");
            return FileDownloadResult.CreateFailure("Download was cancelled");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Failed to download file '{fileName}'");
            return FileDownloadResult.CreateFailure($"Download failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves downloaded file content to disk
    /// </summary>
    /// <param name="filePath">The path where to save the file</param>
    /// <param name="content">The file content</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if successful, false otherwise</returns>
    public async Task<bool> SaveToFileAsync(
        string filePath,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            _logger.Error("Cannot save file: file path is empty");
            return false;
        }

        if (content == null || content.Length == 0)
        {
            _logger.Error("Cannot save file: content is empty");
            return false;
        }

        try
        {
            // Ensure directory exists
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write content to file
            await File.WriteAllBytesAsync(filePath, content, cancellationToken);

            _logger.Information($"Successfully saved file to '{filePath}' ({content.Length} bytes)");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Failed to save file to '{filePath}'");
            return false;
        }
    }
    #endregion

    #region Private Methods
    /// <summary>
    /// Downloads file content from the device
    /// </summary>
    private async Task<byte[]?> DownloadFileContentAsync(
        IStreamingDevice device,
        string fileName,
        CancellationToken cancellationToken)
    {
        // Use Task.Run to make the synchronous device download method async
        return await Task.Run(() =>
        {
            // Check if cancellation was requested
            cancellationToken.ThrowIfCancellationRequested();

            // Cast to AbstractStreamingDevice to access the download method
            if (device is not AbstractStreamingDevice abstractDevice)
            {
                _logger.Error("Device does not support SD card file download");
                return null;
            }

            // Download the file using the device's method
            var content = abstractDevice.DownloadSdCardFile(fileName);

            // Check if cancellation was requested after download
            cancellationToken.ThrowIfCancellationRequested();

            return content;
        }, cancellationToken);
    }
    #endregion
}
