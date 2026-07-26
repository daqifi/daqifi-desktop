namespace Daqifi.Desktop.Loggers;

/// <summary>
/// Thrown when a device accepts an SD card download request and then stops sending data, with the
/// transfer neither completing nor failing. Raised by the desktop's own stall watchdog (see
/// <see cref="SdCardSessionImporter.DOWNLOAD_STALL_TIMEOUT"/>), because Daqifi.Core exposes no
/// timeout on either public <c>DownloadSdCardFileAsync</c> overload.
///
/// Derives from <see cref="TimeoutException"/> so callers that only care that the operation timed
/// out still catch it, while giving the SD failure classifier a type that means specifically
/// "this device went silent". Classifying every <see cref="TimeoutException"/> as a wedged SD
/// subsystem would tell a user to power-cycle their device over an unrelated timeout — and would
/// keep that unrelated timeout off the Error path, where it belongs.
/// </summary>
public sealed class SdCardDownloadStalledException : TimeoutException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SdCardDownloadStalledException"/> class.
    /// </summary>
    /// <param name="fileName">The SD card file whose transfer stalled.</param>
    /// <param name="stallTimeout">How long the device was silent before the download was abandoned.</param>
    public SdCardDownloadStalledException(string fileName, TimeSpan stallTimeout)
        : base($"The device sent no data for '{fileName}' for {stallTimeout.TotalSeconds:N0} seconds, " +
               "so the download was abandoned.")
    {
        FileName = fileName;
        StallTimeout = stallTimeout;
    }

    /// <summary>The SD card file whose transfer stalled.</summary>
    public string FileName { get; }

    /// <summary>How long the device was silent before the download was abandoned.</summary>
    public TimeSpan StallTimeout { get; }
}
