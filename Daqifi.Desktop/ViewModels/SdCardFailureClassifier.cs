using Daqifi.Core.Device.SdCard;

namespace Daqifi.Desktop.ViewModels;

/// <summary>
/// How an SD card operation failed, expressed in terms the UI can act on.
/// </summary>
/// <param name="State">
/// The <see cref="SdCardState"/> the card should be shown in. Only applied when
/// <paramref name="IsExpectedDeviceCondition"/> is <c>true</c> — an unexpected failure
/// (an app bug) says nothing about the card, so the existing state is left alone.
/// </param>
/// <param name="StatusMessage">
/// Terse description bound to <see cref="DeviceLogsViewModel.SdCardErrorMessage"/> (status line
/// and error panel). Empty when the state itself already says everything, as for a missing card.
/// </param>
/// <param name="Guidance">
/// The actionable sentence shown to the user: what they should do about it.
/// </param>
/// <param name="IsExpectedDeviceCondition">
/// <c>true</c> for device/environmental conditions the desktop cannot prevent (no card, wedged SD
/// subsystem, filesystem error). These are logged at Warning so they do not raise Sentry issues;
/// anything else is a genuine defect and keeps the Error path.
/// </param>
/// <param name="IsCardUnavailable">
/// <c>true</c> only when the SD subsystem — not just this one file — is unmistakably unusable, so
/// further file operations against the same device would fail the same way. Batch imports stop
/// early on these rather than retrying every remaining file through the same multi-second failure.
///
/// Deliberately narrow. A condition that <i>might</i> be specific to one file must not set this:
/// <c>ImportAllFiles</c> abandons the rest of the batch, and because the file list comes back in
/// the same order every time, the files after the failing one become unreachable through Import
/// All entirely (issue #780). Skipping the one file and carrying on costs a failed download;
/// aborting costs the user every later healthy file.
/// </param>
public sealed record SdCardFailure(
    SdCardState State,
    string StatusMessage,
    string Guidance,
    bool IsExpectedDeviceCondition,
    bool IsCardUnavailable);

/// <summary>
/// Maps exceptions raised by SD card operations onto the user-facing
/// <see cref="SdCardState"/> surface, and decides whether a failure is an expected device
/// condition (log at Warning, no Sentry issue) or a genuine defect (log at Error).
///
/// Daqifi.Core throws typed, already-actionable exceptions for the device conditions
/// (<see cref="SdCardNotPresentException"/>, <see cref="SdCardEmptyTransferException"/>, …).
/// Before issue #754 the desktop let those escape to the generic Error path, which filed a
/// Sentry issue for what is really "power-cycle the device" — and told the user nothing.
/// </summary>
public static class SdCardFailureClassifier
{
    #region Constants
    /// <summary>Guidance shown when the card is readable but its contents are not.</summary>
    internal const string GENERIC_CARD_GUIDANCE =
        "The card may be corrupt or busy. Try a different card or reformat (FAT32).";

    /// <summary>
    /// Guidance for the wedged-SD-subsystem family. The device answers SCPI and lists files but
    /// serves no data; only a power cycle clears it (firmware issue daqifi-nyquist-firmware#567).
    /// </summary>
    internal const string POWER_CYCLE_GUIDANCE =
        "The device's SD card subsystem is not responding. Power-cycle the device and try again.";

    /// <summary>
    /// Guidance for a transfer that delivered nothing at all for one file. Core cannot tell a
    /// genuinely empty (0-byte) log — routinely left behind on a FAT card by an interrupted logging
    /// session — from an SD subsystem that was not ready when it opened the file, so the advice has
    /// to cover both: this file is skipped, and the power cycle is only worth reaching for when
    /// every file behaves the same way.
    /// </summary>
    internal const string EMPTY_TRANSFER_GUIDANCE =
        "The device sent no data for this file, which may simply be an empty log. If every file " +
        "fails the same way, power-cycle the device and try again.";

    /// <summary>
    /// Guidance for a transfer that stopped part-way. Distinct from
    /// <see cref="EMPTY_TRANSFER_GUIDANCE"/> because a stall can interrupt a transfer that was
    /// already delivering data, and how much arrived is not knowable here — so this must not
    /// diagnose an empty file the way that one does.
    /// </summary>
    internal const string INCOMPLETE_TRANSFER_GUIDANCE =
        "The device stopped sending this file before it was complete. Try importing it on its " +
        "own; if every file fails the same way, power-cycle the device and try again.";

    /// <summary>
    /// Guidance for a transfer whose transport went away mid-file. Distinct from the two above
    /// because the card is not the problem: Core states that retrying on the same transport cannot
    /// succeed, so the user has to re-establish the connection first.
    /// </summary>
    internal const string TRANSPORT_CLOSED_GUIDANCE =
        "The connection to the device was lost during the transfer. Reconnect the device and try again.";

    /// <summary>Guidance for a device with no card in the slot.</summary>
    internal const string NO_CARD_GUIDANCE =
        "No SD card is installed in the device. Insert a card and refresh.";

    /// <summary>Guidance for a failure the desktop could not attribute to the card.</summary>
    internal const string UNEXPECTED_FAILURE_GUIDANCE =
        "Please check the device connection and try again.";
    #endregion

    #region Public Methods
    /// <summary>
    /// Classifies an exception thrown by an SD card refresh, download, or import.
    /// </summary>
    /// <param name="ex">The exception to classify. Never null.</param>
    /// <returns>The UI-facing description of the failure.</returns>
    public static SdCardFailure Classify(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        return ex switch
        {
            SdCardNotPresentException => new SdCardFailure(
                State: SdCardState.NotPresent,
                // The NotPresent panel is self-explanatory, so it carries no secondary message.
                StatusMessage: string.Empty,
                Guidance: NO_CARD_GUIDANCE,
                IsExpectedDeviceCondition: true,
                IsCardUnavailable: true),

            // The device opened the file and closed it again without sending a byte, after Core
            // exhausted its own retries.
            //
            // Since Core v1.4.0 this is size-aware: the receiver compares the marker-only transfer
            // against the directory listing's reported size and only raises this for a file the
            // listing calls non-empty, or whose listed size it could not determine
            // (daqifi-core#398 gap 2). A genuinely 0-byte log — routinely left on a FAT card by an
            // interrupted logging session — now downloads as a legitimate empty file and never
            // reaches here.
            //
            // Still per-file rather than card-wide: the unknown-listed-size case keeps Core's
            // conservative throw, and one wedged file must not make every file listed after it
            // unimportable through Import All (issue #780).
            SdCardEmptyTransferException => new SdCardFailure(
                State: SdCardState.Error,
                StatusMessage: "The device returned no data for this file.",
                Guidance: EMPTY_TRANSFER_GUIDANCE,
                IsExpectedDeviceCondition: true,
                IsCardUnavailable: false),

            // The transfer stopped making progress before the end-of-file marker arrived. Core
            // raises this directly for a transport that returned an empty read or closed; the
            // importer raises it for its own stall watchdog and for Core's hard download deadline
            // (see SdCardSessionImporter.DownloadWithStallWatchdogAsync).
            //
            // Reason is what decides how far the failure generalises — the typed replacement for
            // the desktop's old binary "was the device quiet for the full window" measurement
            // (daqifi-core#398 gap 1). It must precede the SdCardOperationException arm below,
            // which is its base type.
            SdCardTransferStalledException stalled => new SdCardFailure(
                State: SdCardState.Error,
                StatusMessage: stalled.Reason == SdCardTransferStallReason.TransportClosed
                    ? "The connection to the device dropped during the transfer."
                    : "The device stopped responding during the transfer.",
                Guidance: stalled.Reason switch
                {
                    // The whole transfer deadline elapsed with the file incomplete. That is
                    // evidence about the device, and far too expensive to pay again per file.
                    SdCardTransferStallReason.TransferTimeout => POWER_CYCLE_GUIDANCE,

                    // The transport is gone; Core states a retry on it cannot succeed.
                    SdCardTransferStallReason.TransportClosed => TRANSPORT_CLOSED_GUIDANCE,

                    // NoDataReceived — over USB serial this is the ordinary per-read stall and
                    // fires in well under a second, so it can be one unreadable file (issue #779).
                    _ => INCOMPLETE_TRANSFER_GUIDANCE
                },
                IsExpectedDeviceCondition: true,
                // Deliberately narrow (issue #780): only the two reasons that are unmistakably
                // about the device rather than about this one file stop a batch.
                IsCardUnavailable: stalled.Reason is SdCardTransferStallReason.TransferTimeout
                    or SdCardTransferStallReason.TransportClosed),

            // Unambiguously device-wide: the device is using the card itself, so no file on it can
            // be downloaded until logging stops.
            SdCardBusyException => new SdCardFailure(
                State: SdCardState.Error,
                StatusMessage: "The device's SD card is busy.",
                Guidance: "The device is still using the SD card. Stop logging, wait a moment, and try again.",
                IsExpectedDeviceCondition: true,
                IsCardUnavailable: true),

            // A filesystem error can be the whole card or one corrupt directory entry, and the
            // device message does not say which — so skip this file rather than write off the rest.
            SdCardFilesystemException filesystem => new SdCardFailure(
                State: SdCardState.Error,
                StatusMessage: filesystem.DeviceMessage ?? filesystem.Message,
                Guidance: GENERIC_CARD_GUIDANCE,
                IsExpectedDeviceCondition: true,
                IsCardUnavailable: false),

            SdCardOperationException operation => new SdCardFailure(
                State: SdCardState.Error,
                StatusMessage: operation.LastScpiError ?? operation.Message,
                Guidance: GENERIC_CARD_GUIDANCE,
                IsExpectedDeviceCondition: true,
                // A rejected SCPI command can be specific to one file, so the rest of the card
                // is still worth trying.
                IsCardUnavailable: false),

            // A bare TimeoutException is deliberately NOT matched: one reaching here from some
            // other layer is not evidence that the SD subsystem is wedged and must keep the Error
            // path. The importer normalises the one timeout that IS about the card — Core's hard
            // download deadline — onto SdCardTransferStalledException at the call site, where the
            // scope makes it safe.
            _ => new SdCardFailure(
                State: SdCardState.Error,
                StatusMessage: ex.Message,
                Guidance: UNEXPECTED_FAILURE_GUIDANCE,
                IsExpectedDeviceCondition: false,
                IsCardUnavailable: false)
        };
    }
    #endregion
}
