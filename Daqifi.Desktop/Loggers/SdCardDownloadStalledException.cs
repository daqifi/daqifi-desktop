namespace Daqifi.Desktop.Loggers;

/// <summary>
/// Thrown when a device accepts an SD card download request and then fails to deliver the file,
/// with the transfer neither completing nor failing as a device condition Core can name.
///
/// Two different detections raise it — the desktop's own stall watchdog (see
/// <see cref="SdCardSessionImporter.DOWNLOAD_STALL_TIMEOUT"/>), which exists because Daqifi.Core
/// exposes no timeout on either public <c>DownloadSdCardFileAsync</c> overload, and Core reporting
/// a transport-level <see cref="TimeoutException"/> out of the download call (issue #779). Both are
/// measured the same way, by <see cref="SilentFor"/>: how long the device had gone without
/// delivering anything when the attempt was abandoned.
///
/// Derives from <see cref="TimeoutException"/> so callers that only care that the operation timed
/// out still catch it, while giving the SD failure classifier a type that means specifically
/// "this transfer did not complete". Classifying every <see cref="TimeoutException"/> as an SD
/// failure would tell a user to power-cycle their device over an unrelated timeout — and would
/// keep that unrelated timeout off the Error path, where it belongs.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "RCS1194:Implement exception constructors",
    Justification = "This exception exists to carry FileName plus how long the device had been quiet — the " +
                    "classifier reads them, and the message is built from them. The standard parameterless/" +
                    "message-only constructors would allow a stall report that names neither the file nor the " +
                    "silence, so the type is sealed with the single constructor that can produce a meaningful " +
                    "instance.")]
public sealed class SdCardDownloadStalledException : TimeoutException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SdCardDownloadStalledException"/> class.
    /// </summary>
    /// <param name="fileName">The SD card file whose transfer stalled.</param>
    /// <param name="silentFor">
    /// How long the device had delivered nothing when the download was abandoned. For the
    /// desktop's own watchdog this is its full stall window, since that is precisely what the
    /// watchdog waits for.
    /// </param>
    /// <param name="patienceWindow">
    /// The desktop's stall window. Silence lasting at least this long counts as prolonged.
    /// </param>
    /// <param name="transportTimeout">
    /// The timeout Core reported, when the transport detected the stall before the watchdog could.
    /// <c>null</c> when the watchdog raised this itself.
    /// </param>
    public SdCardDownloadStalledException(
        string fileName,
        TimeSpan silentFor,
        TimeSpan patienceWindow,
        TimeoutException? transportTimeout = null)
        : base(BuildMessage(fileName, silentFor, transportTimeout), transportTimeout)
    {
        FileName = fileName;
        SilentFor = silentFor;
        IsProlongedFailure = silentFor >= patienceWindow;
    }

    /// <summary>The SD card file whose transfer stalled.</summary>
    public string FileName { get; }

    /// <summary>
    /// How long the device had delivered nothing when the download was abandoned. Deliberately the
    /// silence, not the total transfer time: a large file that streamed steadily for ten minutes
    /// and then hit one brief transport timeout says nothing bad about the device, and must not be
    /// read the same way as ten minutes of nothing. This is the same thing
    /// <see cref="SdCardSessionImporter.DOWNLOAD_STALL_TIMEOUT"/> bounds.
    /// </summary>
    public TimeSpan SilentFor { get; }

    /// <summary>
    /// <c>true</c> when the device had been quiet for at least the desktop's full stall window.
    /// That makes it evidence about the device rather than about one file, and far too expensive to
    /// repeat for every remaining file — so a batch import gives up on these, and carries on past
    /// the rest.
    /// </summary>
    public bool IsProlongedFailure { get; }

    private static string BuildMessage(string fileName, TimeSpan silentFor, TimeoutException? transportTimeout)
    {
        return transportTimeout == null
            ? $"The device sent no data for '{fileName}' for {silentFor.TotalSeconds:N0} seconds, " +
              "so the download was abandoned."
            : $"The transfer of '{fileName}' stopped before the device signalled end of file, " +
              $"after {silentFor.TotalSeconds:N1} seconds without data.";
    }
}
