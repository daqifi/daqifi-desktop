using Daqifi.Core.Device.SdCard;
using Daqifi.Desktop.Loggers;

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
    /// Guidance for a transfer that delivered nothing for one file. Core deliberately cannot tell a
    /// genuinely empty (0-byte) log — routinely left behind on a FAT card by an interrupted logging
    /// session — from an SD subsystem that was not ready when it opened the file, so the advice has
    /// to cover both: this file is skipped, and the power cycle is only worth reaching for when
    /// every file behaves the same way.
    /// </summary>
    internal const string INCOMPLETE_TRANSFER_GUIDANCE =
        "The device sent no data for this file, which may simply be an empty log. If every file " +
        "fails the same way, power-cycle the device and try again.";

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
            // Core's own doc calls this "never a valid download for a file the directory listing
            // reports as non-empty" — but SdCardFileReceiver has no access to the listed size at
            // the point it throws, so it raises the same exception for any marker-only transfer.
            // A genuinely 0-byte log, which an interrupted logging session routinely leaves on a
            // FAT card, is therefore indistinguishable from a wedged subsystem. Treat it as
            // per-file: one benign empty file used to make every file listed after it unimportable
            // through Import All (issue #780).
            //
            // Revisit once Core makes the empty-transfer check size-aware. See daqifi-core#398 (gap 2).
            SdCardEmptyTransferException => new SdCardFailure(
                State: SdCardState.Error,
                StatusMessage: "The device returned no data for this file.",
                Guidance: INCOMPLETE_TRANSFER_GUIDANCE,
                IsExpectedDeviceCondition: true,
                IsCardUnavailable: false),

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

            // Raised by SdCardSessionImporter when the device stops sending data mid-transfer
            // without ever failing the request. Matched by its specific type, not by
            // TimeoutException: an unrelated timeout reaching here is not evidence that the SD
            // subsystem is wedged, and must keep the Error path.
            //
            // How the stall was detected decides how far it generalises. The desktop's watchdog
            // firing means the device said nothing for 90 seconds — device-wide, and worth
            // aborting a batch over, since every remaining file would cost another 90 seconds. A
            // transport-reported timeout gives up in well under a second and can be one unreadable
            // file, so it is treated like any other per-file failure (issue #779).
            SdCardDownloadStalledException stalled => new SdCardFailure(
                State: SdCardState.Error,
                StatusMessage: "The device stopped responding during the transfer.",
                Guidance: stalled.IsProlongedSilence ? POWER_CYCLE_GUIDANCE : INCOMPLETE_TRANSFER_GUIDANCE,
                IsExpectedDeviceCondition: true,
                IsCardUnavailable: stalled.IsProlongedSilence),

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
