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
/// <c>true</c> when the SD subsystem — not just this one file — is unusable, so further file
/// operations against the same device would fail the same way. Batch imports stop early on these
/// rather than retrying every remaining file through the same multi-second failure.
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

            // The device opened the file and closed it again without sending a byte. Core only
            // throws this after exhausting its own retries, so by the time it reaches us the SD
            // subsystem is wedged rather than merely slow.
            SdCardEmptyTransferException => new SdCardFailure(
                State: SdCardState.Error,
                StatusMessage: "The device returned no data for this file.",
                Guidance: POWER_CYCLE_GUIDANCE,
                IsExpectedDeviceCondition: true,
                IsCardUnavailable: true),

            SdCardBusyException => new SdCardFailure(
                State: SdCardState.Error,
                StatusMessage: "The device's SD card is busy.",
                Guidance: "The device is still using the SD card. Stop logging, wait a moment, and try again.",
                IsExpectedDeviceCondition: true,
                IsCardUnavailable: true),

            SdCardFilesystemException filesystem => new SdCardFailure(
                State: SdCardState.Error,
                StatusMessage: filesystem.DeviceMessage ?? filesystem.Message,
                Guidance: GENERIC_CARD_GUIDANCE,
                IsExpectedDeviceCondition: true,
                IsCardUnavailable: true),

            SdCardOperationException operation => new SdCardFailure(
                State: SdCardState.Error,
                StatusMessage: operation.LastScpiError ?? operation.Message,
                Guidance: GENERIC_CARD_GUIDANCE,
                IsExpectedDeviceCondition: true,
                // A rejected SCPI command can be specific to one file, so the rest of the card
                // is still worth trying.
                IsCardUnavailable: false),

            // Raised by the desktop's own stall watchdog (see SdCardSessionImporter) when the
            // device stops sending data mid-transfer without ever failing the request.
            TimeoutException => new SdCardFailure(
                State: SdCardState.Error,
                StatusMessage: "The device stopped responding during the transfer.",
                Guidance: POWER_CYCLE_GUIDANCE,
                IsExpectedDeviceCondition: true,
                IsCardUnavailable: true),

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
