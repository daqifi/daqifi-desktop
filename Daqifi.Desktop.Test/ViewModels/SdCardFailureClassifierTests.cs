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
    public void Classify_EmptyTransfer_IsExpectedDeviceConditionAndAdvisesPowerCycle()
    {
        // Arrange — the exact exception Core 1.3.0 throws for the wedged SD subsystem, and the
        // one Sentry issue #754 was filed from.
        var ex = new SdCardEmptyTransferException(FileName);

        // Act
        var failure = SdCardFailureClassifier.Classify(ex);

        // Assert
        Assert.IsTrue(failure.IsExpectedDeviceCondition,
            "A wedged SD subsystem is a device condition, so it must log at Warning and not file a Sentry issue.");
        Assert.IsTrue(failure.IsCardUnavailable,
            "Nothing else on the card will download either, so a batch import must stop early.");
        Assert.AreEqual(SdCardState.Error, failure.State);
        Assert.AreEqual(SdCardFailureClassifier.POWER_CYCLE_GUIDANCE, failure.Guidance,
            "Only a power cycle clears this state, so that is what the user must be told.");
    }

    [TestMethod]
    public void Classify_DownloadStalled_IsExpectedDeviceConditionAndAdvisesPowerCycle()
    {
        // Arrange — what the importer's stall watchdog throws when the device goes quiet.
        var ex = new SdCardDownloadStalledException(FileName, TimeSpan.FromSeconds(90));

        // Act
        var failure = SdCardFailureClassifier.Classify(ex);

        // Assert
        Assert.IsTrue(failure.IsExpectedDeviceCondition);
        Assert.IsTrue(failure.IsCardUnavailable);
        Assert.AreEqual(SdCardState.Error, failure.State);
        Assert.AreEqual(SdCardFailureClassifier.POWER_CYCLE_GUIDANCE, failure.Guidance);
    }

    [TestMethod]
    public void Classify_UnrelatedTimeout_KeepsTheErrorPath()
    {
        // Arrange — a timeout from some other layer (a database call, an HTTP request) that
        // happens to reach an SD catch block. Regression guard: matching TimeoutException by base
        // type would tell the user to power-cycle a healthy device over an unrelated failure, and
        // would keep that failure off the Error path where it belongs.
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
        Assert.IsTrue(failure.IsCardUnavailable);
    }

    [TestMethod]
    public void Classify_FilesystemError_SurfacesTheDeviceMessage()
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
