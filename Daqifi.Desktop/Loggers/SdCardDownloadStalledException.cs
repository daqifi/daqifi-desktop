namespace Daqifi.Desktop.Loggers;

/// <summary>
/// Thrown when a device accepts an SD card download request and then fails to deliver the file,
/// with the transfer neither completing nor failing as a device condition Core can name.
///
/// Two different detections raise it, and they carry different weight:
/// <list type="bullet">
/// <item>
/// The desktop's own stall watchdog (see <see cref="SdCardSessionImporter.DOWNLOAD_STALL_TIMEOUT"/>)
/// observing prolonged silence. Daqifi.Core exposes no timeout on either public
/// <c>DownloadSdCardFileAsync</c> overload, so the desktop has to impose that bound itself.
/// <see cref="StallTimeout"/> is set and <see cref="IsProlongedSilence"/> is <c>true</c>: a device
/// that has said nothing for that long is not going to serve the next file either.
/// </item>
/// <item>
/// Core reporting a transport-level <see cref="TimeoutException"/> out of the download call.
/// <see cref="StallTimeout"/> is <c>null</c>, the transport's own exception is the
/// <see cref="Exception.InnerException"/>, and <see cref="IsProlongedSilence"/> is <c>false</c> —
/// this can be one bad file rather than a wedged subsystem (issue #779).
/// </item>
/// </list>
///
/// Derives from <see cref="TimeoutException"/> so callers that only care that the operation timed
/// out still catch it, while giving the SD failure classifier a type that means specifically
/// "this transfer did not complete". Classifying every <see cref="TimeoutException"/> as an SD
/// failure would tell a user to power-cycle their device over an unrelated timeout — and would
/// keep that unrelated timeout off the Error path, where it belongs.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "RCS1194:Implement exception constructors",
    Justification = "This exception exists to carry FileName plus how the stall was detected — the classifier " +
                    "reads them, and the message is built from them. The standard parameterless/message-only " +
                    "constructors would allow a stall report that names neither the file nor the detection, so " +
                    "the type is sealed with the two constructors that can produce a meaningful instance.")]
public sealed class SdCardDownloadStalledException : TimeoutException
{
    /// <summary>
    /// Initializes a stall detected by the desktop's own watchdog: the device delivered nothing at
    /// all for <paramref name="stallTimeout"/>.
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

    /// <summary>
    /// Initializes a stall the transport detected first, before the watchdog could: Core's
    /// <c>SdCardFileReceiver</c> raises a plain <see cref="TimeoutException"/> as soon as a read
    /// returns no bytes, which over USB serial happens within about half a second.
    /// </summary>
    /// <param name="fileName">The SD card file whose transfer stalled.</param>
    /// <param name="transportTimeout">The timeout Core reported, kept as the inner exception.</param>
    public SdCardDownloadStalledException(string fileName, TimeoutException transportTimeout)
        : base($"The transfer of '{fileName}' stopped before the device signalled end of file.",
               transportTimeout)
    {
        FileName = fileName;
        StallTimeout = null;
    }

    /// <summary>The SD card file whose transfer stalled.</summary>
    public string FileName { get; }

    /// <summary>
    /// How long the device was silent before the desktop's watchdog abandoned the download, or
    /// <c>null</c> when the transport reported the timeout before the watchdog could fire.
    /// </summary>
    public TimeSpan? StallTimeout { get; }

    /// <summary>
    /// <c>true</c> when the desktop watched the device say nothing for <see cref="StallTimeout"/>.
    /// That is unambiguously device-wide, so a batch import gives up rather than spend the same
    /// wait on every remaining file. A transport-reported timeout is not: it can be a single
    /// unreadable file, and gives up in well under a second, so the batch skips it and continues.
    /// </summary>
    public bool IsProlongedSilence => StallTimeout.HasValue;
}
