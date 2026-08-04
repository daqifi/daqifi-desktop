using Daqifi.Core.Device.SdCard;
using Daqifi.Desktop.Loggers;
using Daqifi.Desktop.ViewModels;

namespace Daqifi.Desktop.Test.ViewModels;

/// <summary>
/// Covers the classification that decides how an SD card failure reaches the user and the log.
/// The load-bearing distinction is <c>IsExpectedDeviceCondition</c>: it routes a failure to
/// Warning (local log only) instead of Error (captured to Sentry). Before issue #754 a wedged SD
/// subsystem — which only a power cycle fixes — filed a Sentry issue on every import attempt.
/// </summary>
[TestClass]
public class SdCardFailureClassifierTests
{
    private const string FileName = "log_20260623_143217.bin";

    [TestMethod]
    public void Classify_EmptyTransfer_IsExpectedDeviceConditionButDoesNotWriteOffTheCard()
    {
        // Arrange — what Core still throws when the device serves a marker-only transfer for a file
        // its listing called non-empty (or whose listed size it could not determine), and the one
        // Sentry issue #754 was filed from. A listed 0-byte file no longer reaches here at all —
        // Core v1.4.0 returns it as a legitimate empty download (daqifi-core#398 gap 2).
        var ex = new SdCardEmptyTransferException(FileName, listedSizeInBytes: 4096);

        // Act
        var failure = SdCardFailureClassifier.Classify(ex);

        // Assert
        Assert.IsTrue(failure.IsExpectedDeviceCondition,
            "A device that serves no data is a device condition, so it must log at Warning and not " +
            "file a Sentry issue.");
        Assert.IsFalse(failure.IsCardUnavailable,
            "Issue #780: Core's unknown-listed-size case keeps its conservative throw, so this can " +
            "still be one file rather than the card, and must not strand every file listed after it.");
        Assert.AreEqual(SdCardState.Error, failure.State);
        Assert.AreEqual(SdCardFailureClassifier.EMPTY_TRANSFER_GUIDANCE, failure.Guidance,
            "The advice has to cover both readings, not send the user after a power cycle they may not need.");
    }

    [TestMethod]
    public void Classify_TransferTimeoutStall_AdvisesPowerCycleAndStopsTheBatch()
    {
        // Arrange — the reason both the importer's own stall watchdog and Core's hard download
        // deadline report: a transfer deadline elapsed with the file incomplete. That is
        // unambiguously device-wide, so it is the one download failure worth abandoning the rest of
        // the batch over.
        var ex = new SdCardTransferStalledException(
            FileName,
            bytesReceived: 0,
            SdCardTransferStallReason.TransferTimeout,
            TimeSpan.FromSeconds(90));

        // Act
        var failure = SdCardFailureClassifier.Classify(ex);

        // Assert
        Assert.IsTrue(failure.IsExpectedDeviceCondition);
        Assert.IsTrue(failure.IsCardUnavailable,
            "90 seconds of total silence per file makes grinding through the rest of the batch pointless and slow.");
        Assert.AreEqual(SdCardState.Error, failure.State);
        Assert.AreEqual(SdCardFailureClassifier.POWER_CYCLE_GUIDANCE, failure.Guidance);
    }

    [TestMethod]
    public void Classify_QuickTransportStall_IsExpectedDeviceConditionAndKeepsTheBatchGoing()
    {
        // Arrange — issue #779: over USB serial Core's SdCardFileReceiver gives up on a zero-length
        // read within about half a second, long before any deadline. Core v1.4.0 types it as
        // NoDataReceived (daqifi-core#398 gap 1); it must stay off the Sentry path, but it gives up
        // too fast and too ambiguously to justify writing off the whole card.
        var ex = new SdCardTransferStalledException(
            FileName, bytesReceived: 0, SdCardTransferStallReason.NoDataReceived);

        // Act
        var failure = SdCardFailureClassifier.Classify(ex);

        // Assert
        Assert.IsTrue(failure.IsExpectedDeviceCondition,
            "This is the #754 condition on the transport that actually ships, so it must stay off the Error path.");
        Assert.IsFalse(failure.IsCardUnavailable,
            "It costs under a second to try the next file, and this one may just be unreadable.");
        Assert.AreEqual(SdCardState.Error, failure.State);
        Assert.AreEqual(SdCardFailureClassifier.INCOMPLETE_TRANSFER_GUIDANCE, failure.Guidance);
        Assert.AreNotEqual(SdCardFailureClassifier.EMPTY_TRANSFER_GUIDANCE, failure.Guidance,
            "A stall can interrupt a transfer that was already delivering data, so the advice must " +
            "not diagnose an empty file the way the empty-transfer arm does.");
    }

    [TestMethod]
    public void Classify_TransportClosedStall_TellsTheUserToReconnectAndStopsTheBatch()
    {
        // Arrange — Core's third stall reason: the transport went away underneath the transfer.
        // Core's own contract says retrying the download on it cannot succeed, so grinding through
        // the remaining files would only produce the same failure — but the advice is to reconnect,
        // not to power-cycle a card that was never the problem.
        var ex = new SdCardTransferStalledException(
            FileName, bytesReceived: 4096, SdCardTransferStallReason.TransportClosed);

        // Act
        var failure = SdCardFailureClassifier.Classify(ex);

        // Assert
        Assert.IsTrue(failure.IsExpectedDeviceCondition,
            "A dropped connection is a device/environmental condition, not an app defect.");
        Assert.IsTrue(failure.IsCardUnavailable);
        Assert.AreEqual(SdCardFailureClassifier.TRANSPORT_CLOSED_GUIDANCE, failure.Guidance);
    }

    [TestMethod]
    public void Classify_Stall_IsMatchedBeforeItsOperationExceptionBaseType()
    {
        // Arrange — regression guard on switch-arm order. SdCardTransferStalledException derives
        // from SdCardOperationException, so an arm added above it would swallow every stall into
        // the generic "card may be corrupt" advice and drop the reason entirely.
        var ex = new SdCardTransferStalledException(
            FileName,
            bytesReceived: 0,
            SdCardTransferStallReason.TransferTimeout,
            TimeSpan.FromSeconds(90));

        // Act
        var failure = SdCardFailureClassifier.Classify(ex);

        // Assert
        Assert.AreNotEqual(SdCardFailureClassifier.GENERIC_CARD_GUIDANCE, failure.Guidance);
        Assert.IsTrue(failure.IsCardUnavailable,
            "The SdCardOperationException arm reports IsCardUnavailable: false, so reaching it " +
            "would silently turn a device-wide stall into a per-file one.");
    }

    [TestMethod]
    public void Classify_UnrelatedTimeout_KeepsTheErrorPath()
    {
        // Arrange — a timeout from some other layer (a database call, an HTTP request) that
        // happens to reach an SD catch block. Regression guard: matching TimeoutException by base
        // type would tell the user to power-cycle a healthy device over an unrelated failure, and
        // would keep that failure off the Error path where it belongs. Core's own hard download
        // deadline is the one timeout that IS about the card, and the importer normalises it at
        // the call site, where the scope makes it safe.
        var ex = new TimeoutException("The operation has timed out.");

        // Act
        var failure = SdCardFailureClassifier.Classify(ex);

        // Assert
        Assert.IsFalse(failure.IsExpectedDeviceCondition);
        Assert.IsFalse(failure.IsCardUnavailable);
        Assert.AreNotEqual(SdCardFailureClassifier.POWER_CYCLE_GUIDANCE, failure.Guidance);
    }

    [TestMethod]
    public void Classify_NotPresent_MapsToNotPresentStateWithNoSecondaryMessage()
    {
        // Arrange
        var ex = new SdCardNotPresentException(new List<string>(), "No card");

        // Act
        var failure = SdCardFailureClassifier.Classify(ex);

        // Assert
        Assert.AreEqual(SdCardState.NotPresent, failure.State);
        Assert.AreEqual(string.Empty, failure.StatusMessage,
            "The 'no card installed' panel is self-explanatory; a raw device string would only add noise.");
        Assert.IsTrue(failure.IsExpectedDeviceCondition);
        Assert.IsTrue(failure.IsCardUnavailable,
            "An empty slot is unambiguously device-wide: no file on it can be read.");
    }

    [TestMethod]
    public void Classify_Busy_IsUnambiguouslyDeviceWide()
    {
        // Arrange — the device is logging to the card itself, so nothing on it can be served.
        // Along with a missing card this is one of only two conditions broad enough to abandon a
        // batch import over (issue #780).
        var ex = new SdCardBusyException(new List<string>(), "Card busy");

        // Act
        var failure = SdCardFailureClassifier.Classify(ex);

        // Assert
        Assert.AreEqual(SdCardState.Error, failure.State);
        Assert.IsTrue(failure.IsExpectedDeviceCondition);
        Assert.IsTrue(failure.IsCardUnavailable);
    }

    [TestMethod]
    public void Classify_FilesystemError_SurfacesTheDeviceMessageAndKeepsTheRestOfTheCardUsable()
    {
        // Arrange
        const string deviceMessage = "FS corrupt";
        var ex = new SdCardFilesystemException(new List<string>(), "Filesystem error", deviceMessage);

        // Act
        var failure = SdCardFailureClassifier.Classify(ex);

        // Assert
        Assert.AreEqual(SdCardState.Error, failure.State);
        Assert.AreEqual(deviceMessage, failure.StatusMessage);
        Assert.IsTrue(failure.IsExpectedDeviceCondition);
        Assert.IsFalse(failure.IsCardUnavailable,
            "Issue #780: a filesystem error can be one corrupt directory entry, so the other files " +
            "are still worth trying rather than being dropped unattempted.");
        Assert.AreEqual(SdCardFailureClassifier.GENERIC_CARD_GUIDANCE, failure.Guidance);
    }

    [TestMethod]
    public void Classify_OperationError_SurfacesScpiErrorAndKeepsTheRestOfTheCardUsable()
    {
        // Arrange
        const string scpiError = "-200,\"Execution error\"";
        var ex = new SdCardOperationException("SCPI error", new List<string>(), scpiError, null);

        // Act
        var failure = SdCardFailureClassifier.Classify(ex);

        // Assert
        Assert.AreEqual(scpiError, failure.StatusMessage);
        Assert.IsTrue(failure.IsExpectedDeviceCondition);
        Assert.IsFalse(failure.IsCardUnavailable,
            "A rejected command can be specific to one file, so the remaining files are still worth trying.");
    }

    [TestMethod]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2201:Do not raise reserved exception types",
        Justification = "The reserved exception is test input, not something this code raises: it stands in " +
                        "for the runtime-thrown defect the classifier must keep on the Error path. CA2201 " +
                        "guards against throwing these; substituting a non-reserved type would make the " +
                        "regression guard less representative of the failure it exists to catch.")]
    public void Classify_UnknownException_KeepsTheErrorPath()
    {
        // Arrange — a defect in the import pipeline, not a device condition. Regression guard:
        // downgrading these too would silence the Sentry reporting that catches real bugs.
        const string message = "Object reference not set to an instance of an object.";
        var ex = new NullReferenceException(message);

        // Act
        var failure = SdCardFailureClassifier.Classify(ex);

        // Assert
        Assert.IsFalse(failure.IsExpectedDeviceCondition,
            "An unrecognised failure must keep logging at Error so it still reaches Sentry.");
        Assert.IsFalse(failure.IsCardUnavailable);
        Assert.AreEqual(message, failure.StatusMessage);
    }
}
