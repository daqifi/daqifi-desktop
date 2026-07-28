namespace Daqifi.Desktop.Loggers;

/// <summary>
/// Thrown when a device accepts an SD card download request and then fails to deliver the file,
/// with the transfer neither completing nor failing as a device condition Core can name.
///
/// Two different detections raise it — the desktop's own stall watchdog (see
/// <see cref="SdCardSessionImporter.DOWNLOAD_STALL_TIMEOUT"/>), which exists because Daqifi.Core
/// exposes no timeout on either public <c>DownloadSdCardFileAsync</c> overload, and Core reporting
/// a transport-level <see cref="TimeoutException"/> out of the download call (issue #779). What
/// separates them for the caller is not which fired but <see cref="IsProlongedFailure"/>: how much
/// the attempt cost before it gave up.
///
/// Derives from <see cref="TimeoutException"/> so callers that only care that the operation timed
/// out still catch it, while giving the SD failure classifier a type that means specifically
/// "this transfer did not complete". Classifying every <see cref="TimeoutException"/> as an SD
/// failure would tell a user to power-cycle their device over an unrelated timeout — and would
/// keep that unrelated timeout off the Error path, where it belongs.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "RCS1194:Implement exception constructors",
    Justification = "This exception exists to carry FileName plus what the failed attempt cost — the classifier " +
                    "reads them, and the message is built from them. The standard parameterless/message-only " +
                    "constructors would allow a stall report that names neither the file nor the duration, so " +
                    "the type is sealed with the two constructors that can produce a meaningful instance.")]
public sealed class SdCardDownloadStalledException : TimeoutException
{
    /// <summary>
    /// Initializes a stall detected by the desktop's own watchdog: the device delivered nothing at
    /// all for <paramref name="stallTimeout"/>, which is by definition a prolonged failure.
    /// </summary>
    /// <param name="fileName">The SD card file whose transfer stalled.</param>
    /// <param name="stallTimeout">How long the device was silent before the download was abandoned.</param>
    public SdCardDownloadStalledException(string fileName, TimeSpan stallTimeout)
        : base($"The device sent no data for '{fileName}' for {stallTimeout.TotalSeconds:N0} seconds, " +
               "so the download was abandoned.")
    {
        FileName = fileName;
        Elapsed = stallTimeout;
        IsProlongedFailure = true;
    }

    /// <summary>
    /// Initializes a stall the transport reported before the watchdog could: Core's
    /// <c>SdCardFileReceiver</c> raises a plain <see cref="TimeoutException"/> as soon as a read
    /// returns no bytes, which over USB serial happens within about half a second. Core also
    /// raises one from its own 30-minute transfer cap, which is the same type but nothing like
    /// the same cost — hence <paramref name="elapsed"/>.
    /// </summary>
    /// <param name="fileName">The SD card file whose transfer stalled.</param>
    /// <param name="transportTimeout">The timeout Core reported, kept as the inner exception.</param>
    /// <param name="elapsed">How long the download ran before Core gave up on it.</param>
    /// <param name="patienceWindow">
    /// The desktop's own stall window. An attempt that ran at least this long counts as prolonged.
    /// </param>
    public SdCardDownloadStalledException(
        string fileName,
        TimeoutException transportTimeout,
        TimeSpan elapsed,
        TimeSpan patienceWindow)
        : base($"The transfer of '{fileName}' stopped after {elapsed.TotalSeconds:N1} seconds, " +
               "before the device signalled end of file.",
               transportTimeout)
    {
        FileName = fileName;
        Elapsed = elapsed;

        // Core reports its half-second serial read timeout and its 30-minute transfer cap through
        // the identical exception type, so duration is the only thing separating "cheap, and
        // possibly just this file" from "expensive, and evidence about the device". Anything that
        // already cost the desktop its full stall window is the latter.
        IsProlongedFailure = elapsed >= patienceWindow;
    }

    /// <summary>The SD card file whose transfer stalled.</summary>
    public string FileName { get; }

    /// <summary>How long the download ran before it was abandoned.</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>
    /// <c>true</c> when the attempt consumed at least the desktop's full stall window before
    /// failing. That makes it both evidence about the device rather than about one file, and far
    /// too expensive to repeat for every remaining file — so a batch import gives up on these and
    /// carries on past the rest.
    /// </summary>
    public bool IsProlongedFailure { get; }
}
