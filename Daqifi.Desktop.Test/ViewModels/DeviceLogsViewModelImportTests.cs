using Daqifi.Core.Device.SdCard;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.Loggers;
using Daqifi.Desktop.Models;
using Daqifi.Desktop.ViewModels;
using Moq;

namespace Daqifi.Desktop.Test.ViewModels;

/// <summary>
/// Covers how <see cref="DeviceLogsViewModel"/> reports a failed SD card import.
///
/// Issue #754: importing from a device whose SD subsystem is wedged let Core's typed
/// <see cref="SdCardEmptyTransferException"/> escape to the generic Error path. That filed a
/// Sentry issue for a condition only a power cycle fixes, and told the user nothing beyond
/// "check the device connection". These tests pin the replacement behaviour: expected device
/// conditions log at Warning, never at Error, and land in the SD card status surface the view
/// already binds to.
/// </summary>
[TestClass]
public class DeviceLogsViewModelImportTests
{
    private const string FileName = "log_20260623_143217.bin";

    /// <summary>A three-file card, in the order the device lists them.</summary>
    private static readonly string[] THREE_FILES = ["log_a.bin", "log_b.bin", "log_c.bin"];

    private Mock<IStreamingDevice> _mockDevice = null!;
    private Mock<ISdCardSessionImporter> _mockImporter = null!;
    private Mock<IAppLogger> _mockLogger = null!;
    private List<LoggingSession> _publishedSessions = null!;
    private DeviceLogsViewModel _viewModel = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockDevice = new Mock<IStreamingDevice>();
        _mockDevice.Setup(d => d.ConnectionType).Returns(ConnectionType.Usb);
        _mockDevice.Setup(d => d.DeviceSerialNo).Returns("DAQ-TEST-001");
        _mockDevice.Setup(d => d.SdCardFiles).Returns(new List<SdCardFile>().AsReadOnly());

        _mockImporter = new Mock<ISdCardSessionImporter>();
        _mockLogger = new Mock<IAppLogger>();

        // Production publishes to LoggingManager.Instance, whose constructor resolves services
        // from App.ServiceProvider and therefore throws under test. Collect them here instead.
        _publishedSessions = [];

        // DeviceLogsViewModel's constructor reads the process-wide ConnectionManager.Instance
        // singleton; other test classes leave devices on it, so reset it for order-independence.
        ConnectionManager.Instance.ConnectedDevices.Clear();

        _viewModel = new DeviceLogsViewModel(_mockLogger.Object, _mockImporter.Object, _publishedSessions.Add)
        {
            SelectedDevice = _mockDevice.Object
        };
    }

    [TestCleanup]
    public void Cleanup()
    {
        ConnectionManager.Instance.ConnectedDevices.Clear();
    }

    /// <summary>
    /// Selecting a USB device kicks off an auto-refresh that ends by setting the card state to Ok.
    /// Awaiting it keeps that late write from racing the state each test asserts on.
    /// </summary>
    private async Task SettleInitialRefreshAsync()
    {
        if (_viewModel.InitialRefreshTask != null)
        {
            await _viewModel.InitialRefreshTask;
        }
    }

    private void SetupImportToThrow(Exception ex)
    {
        _mockImporter
            .Setup(i => i.ImportFromDeviceAsync(
                It.IsAny<IStreamingDevice>(),
                It.IsAny<string>(),
                It.IsAny<ImportOptions>(),
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(ex);
    }

    private Task ImportAsync(string fileName = FileName) =>
        _viewModel.ImportFileCommand.ExecuteAsync(new SdCardFile { FileName = fileName });

    /// <summary>
    /// Drives the importer per file: the named files throw <paramref name="failure"/>, every other
    /// file imports cleanly. Lets a batch test show that a failure on one file does not cost the
    /// user the ones after it.
    /// </summary>
    private void SetupImportToFailOnly(Exception failure, params string[] failingFiles)
    {
        var sessionId = 0;
        _mockImporter
            .Setup(i => i.ImportFromDeviceAsync(
                It.IsAny<IStreamingDevice>(),
                It.IsAny<string>(),
                It.IsAny<ImportOptions>(),
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task<SdCardImportResult> (IStreamingDevice _, string fileName, ImportOptions _,
                IProgress<ImportProgress> _, CancellationToken _) =>
            {
                if (failingFiles.Contains(fileName))
                {
                    throw failure;
                }

                return Task.FromResult(new SdCardImportResult
                {
                    Session = new LoggingSession(sessionId++, $"SD Import - {fileName}"),
                    TimestampQuality = new ImportTimestampQuality()
                });
            });
    }

    private void AddDeviceFiles(params string[] fileNames)
    {
        foreach (var name in fileNames)
        {
            _viewModel.DeviceFiles.Add(new SdCardFile { FileName = name });
        }
    }

    /// <summary>
    /// The stall a wedged device actually produces over USB serial: Core's transport timeout,
    /// normalised by the importer, having given up in well under the desktop's stall window.
    /// </summary>
    private static SdCardDownloadStalledException QuickTransportStall(string fileName) =>
        new(fileName,
            new TimeoutException("Transport stream closed before receiving the EOF marker."),
            elapsed: TimeSpan.FromMilliseconds(500),
            patienceWindow: TimeSpan.FromSeconds(90));

    private IEnumerable<string> ImportedFileNames() =>
        _mockImporter.Invocations
            .Where(invocation => invocation.Method.Name == nameof(ISdCardSessionImporter.ImportFromDeviceAsync))
            .Select(invocation => (string)invocation.Arguments[1]);

    [TestMethod]
    public async Task ImportFile_EmptyTransfer_LogsWarningNotError()
    {
        // Arrange — the exception Sentry issue #754 was filed from.
        await SettleInitialRefreshAsync();
        SetupImportToThrow(new SdCardEmptyTransferException(FileName));

        // Act
        await ImportAsync();

        // Assert
        _mockLogger.Verify(l => l.Warning(It.IsAny<Exception>(), It.IsAny<string>()), Times.Once,
            "A wedged SD subsystem is an expected device condition and belongs in the local log.");
        _mockLogger.Verify(l => l.Error(It.IsAny<Exception>(), It.IsAny<string>()), Times.Never,
            "Logging at Error captures to Sentry, which is exactly what issue #754 was about.");
        _mockLogger.Verify(l => l.Error(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task ImportFile_EmptyTransfer_LeavesTheFileListVisibleForAPerFileRetry()
    {
        // Arrange
        await SettleInitialRefreshAsync();
        AddDeviceFiles("log_a.bin", "log_b.bin");
        SetupImportToThrow(new SdCardEmptyTransferException(FileName));

        // Act
        await ImportAsync();

        // Assert — issue #780: the SD card error panel *replaces* the file list, so painting one
        // file's failure onto it takes away the per-file retry that skipping the file is supposed
        // to leave available. The failure still reaches the user through the import dialog.
        Assert.AreEqual(SdCardState.Ok, _viewModel.SdCardState);
        Assert.IsFalse(_viewModel.HasSdCardError);
        Assert.IsTrue(_viewModel.HasFiles, "The remaining files must stay reachable one by one.");
    }

    [TestMethod]
    public async Task ImportFile_WatchdogDetectedStall_SurfacesPowerCycleGuidanceOnTheCardPanel()
    {
        // Arrange — what the importer's stall watchdog raises when the device goes quiet for the
        // full window. That is card-wide, so it does belong on the panel.
        await SettleInitialRefreshAsync();
        SetupImportToThrow(new SdCardDownloadStalledException(FileName, TimeSpan.FromSeconds(90)));

        // Act
        await ImportAsync();

        // Assert
        _mockLogger.Verify(l => l.Warning(It.IsAny<Exception>(), It.IsAny<string>()), Times.Once);
        _mockLogger.Verify(l => l.Error(It.IsAny<Exception>(), It.IsAny<string>()), Times.Never);
        Assert.AreEqual(SdCardState.Error, _viewModel.SdCardState);
        Assert.IsTrue(_viewModel.HasSdCardError);
        Assert.AreEqual(SdCardFailureClassifier.POWER_CYCLE_GUIDANCE, _viewModel.SdCardErrorGuidance);
        Assert.IsFalse(string.IsNullOrEmpty(_viewModel.SdCardErrorMessage));
    }

    [TestMethod]
    public async Task ImportFile_TransportDetectedStall_LogsWarningNotError()
    {
        // Arrange — issue #779: over USB serial this, not the watchdog, is what a wedged device
        // actually produces. Before the importer normalised it, the bare TimeoutException hit the
        // classifier's default arm and filed a Sentry issue on every attempt.
        await SettleInitialRefreshAsync();
        SetupImportToThrow(QuickTransportStall(FileName));

        // Act
        await ImportAsync();

        // Assert
        _mockLogger.Verify(l => l.Warning(It.IsAny<Exception>(), It.IsAny<string>()), Times.Once);
        _mockLogger.Verify(l => l.Error(It.IsAny<Exception>(), It.IsAny<string>()), Times.Never,
            "The whole point of #779 is that this stopped reaching the Sentry path.");
        _mockLogger.Verify(l => l.Error(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task ImportFile_NotPresent_ShowsTheMissingCardState()
    {
        // Arrange
        await SettleInitialRefreshAsync();
        SetupImportToThrow(new SdCardNotPresentException(new List<string>(), "No card"));

        // Act
        await ImportAsync();

        // Assert
        Assert.AreEqual(SdCardState.NotPresent, _viewModel.SdCardState);
        Assert.IsTrue(_viewModel.HasSdCardNotPresent);
        _mockLogger.Verify(l => l.Warning(It.IsAny<Exception>(), It.IsAny<string>()), Times.Once);
        _mockLogger.Verify(l => l.Error(It.IsAny<Exception>(), It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task ImportFile_UnexpectedFailure_KeepsErrorLoggingAndDoesNotBlameTheCard()
    {
        // Arrange — a defect in the import pipeline, not a device condition. Regression guard: the
        // #754 fix must not silence real bugs, nor hide a healthy file list behind an error panel.
        await SettleInitialRefreshAsync();
        Assert.AreEqual(SdCardState.Ok, _viewModel.SdCardState, "Precondition: the card refreshed cleanly.");
        SetupImportToThrow(new InvalidOperationException("Bulk insert failed."));

        // Act
        await ImportAsync();

        // Assert
        _mockLogger.Verify(l => l.Error(It.IsAny<Exception>(), It.IsAny<string>()), Times.Once,
            "An unrecognised failure must still reach Sentry.");
        _mockLogger.Verify(l => l.Warning(It.IsAny<Exception>(), It.IsAny<string>()), Times.Never);
        Assert.AreEqual(SdCardState.Ok, _viewModel.SdCardState,
            "A failure the desktop cannot attribute to the card must leave the card state alone.");
    }

    [TestMethod]
    public async Task ImportFile_Failure_ClearsTheBusyOverlay()
    {
        // Arrange — the #754 symptom was a busy overlay that never went away.
        await SettleInitialRefreshAsync();
        SetupImportToThrow(new SdCardEmptyTransferException(FileName));

        // Act
        await ImportAsync();

        // Assert
        Assert.IsFalse(_viewModel.IsBusy);
        Assert.IsTrue(string.IsNullOrEmpty(_viewModel.BusyMessage));
    }

    [TestMethod]
    public async Task ImportFile_WhenSelectionChangesMidImport_UsesTheOriginalDeviceAndLeavesTheNewOneAlone()
    {
        // Arrange — an import runs for many seconds on a background thread, so the user can select
        // a different device while it is in flight. Re-reading SelectedDevice inside the lambda
        // would download from whichever device is selected by then, and paint the first device's
        // failure onto the second device's panel.
        await SettleInitialRefreshAsync();

        var otherDevice = new Mock<IStreamingDevice>();
        otherDevice.Setup(d => d.ConnectionType).Returns(ConnectionType.Usb);
        otherDevice.Setup(d => d.DeviceSerialNo).Returns("DAQ-TEST-002");
        otherDevice.Setup(d => d.SdCardFiles).Returns(new List<SdCardFile>().AsReadOnly());

        IStreamingDevice? importedFrom = null;
        _mockImporter
            .Setup(i => i.ImportFromDeviceAsync(
                It.IsAny<IStreamingDevice>(),
                It.IsAny<string>(),
                It.IsAny<ImportOptions>(),
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task<SdCardImportResult> (IStreamingDevice device, string _, ImportOptions _,
                IProgress<ImportProgress> _, CancellationToken _) =>
            {
                importedFrom = device;
                _viewModel.SelectedDevice = otherDevice.Object;
                throw new SdCardEmptyTransferException(FileName);
            });

        // Act
        await ImportAsync();
        await SettleInitialRefreshAsync();

        // Assert
        Assert.AreSame(_mockDevice.Object, importedFrom,
            "The import must run against the device that was selected when it started.");
        Assert.AreEqual(SdCardState.Ok, _viewModel.SdCardState,
            "The first device's failure must not be shown against the newly selected device.");
    }

    [TestMethod]
    public async Task ImportAllFiles_WhenCardIsUnavailable_StopsAfterTheFirstFile()
    {
        // Arrange — three files on a device with no card in the slot. Nothing on it can be read,
        // and each attempt burns a multi-second timeout, so the batch must give up rather than
        // make the user wait through all of them.
        await SettleInitialRefreshAsync();
        AddDeviceFiles(THREE_FILES);

        SetupImportToThrow(new SdCardNotPresentException(new List<string>(), "No card"));

        // Act
        await _viewModel.ImportAllFilesCommand.ExecuteAsync(null);

        // Assert
        _mockImporter.Verify(
            i => i.ImportFromDeviceAsync(
                It.IsAny<IStreamingDevice>(),
                It.IsAny<string>(),
                It.IsAny<ImportOptions>(),
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "Once the card is known to be unavailable the remaining files must not be attempted.");
        _mockLogger.Verify(l => l.Error(It.IsAny<Exception>(), It.IsAny<string>()), Times.Never);
        Assert.IsFalse(_viewModel.IsBusy);
    }

    [TestMethod]
    public async Task ImportAllFiles_WhenOneFileFails_StillTriesTheRest()
    {
        // Arrange — a per-file SCPI rejection says nothing about the other files, so the batch
        // must carry on. Counterpart to the early-abort case above.
        await SettleInitialRefreshAsync();
        AddDeviceFiles(THREE_FILES);

        SetupImportToThrow(new SdCardOperationException(
            "SCPI error", new List<string>(), "-200,\"Execution error\"", null));

        // Act
        await _viewModel.ImportAllFilesCommand.ExecuteAsync(null);

        // Assert
        _mockImporter.Verify(
            i => i.ImportFromDeviceAsync(
                It.IsAny<IStreamingDevice>(),
                It.IsAny<string>(),
                It.IsAny<ImportOptions>(),
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(3));
        _mockLogger.Verify(l => l.Error(It.IsAny<Exception>(), It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task ImportAllFiles_WhenTheFirstFileIsEmpty_StillImportsEveryHealthyFileAfterIt()
    {
        // Arrange — issue #780, the whole point of the change. An interrupted logging session
        // routinely leaves a 0-byte file on a FAT card, and Core raises the same exception for it
        // as for a wedged subsystem. Aborting on it dropped every later file silently, and since
        // the device lists files in the same order every time, a retry stopped at the same file —
        // making the rest unreachable through Import All entirely.
        await SettleInitialRefreshAsync();
        AddDeviceFiles(THREE_FILES);

        SetupImportToFailOnly(new SdCardEmptyTransferException("log_a.bin"), "log_a.bin");

        // Act
        await _viewModel.ImportAllFilesCommand.ExecuteAsync(null);

        // Assert
        CollectionAssert.AreEqual(
            THREE_FILES,
            ImportedFileNames().ToList(),
            "Every file must be attempted; the empty one is skipped, not treated as the end of the card.");
        Assert.AreEqual(2, _publishedSessions.Count,
            "Both healthy files must actually have been imported.");
        _mockLogger.Verify(l => l.Error(It.IsAny<Exception>(), It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task ImportAllFiles_WhenAFileIsSkipped_LeavesTheCardPanelAndFileListAlone()
    {
        // Arrange — a skipped file must not put the card into an error state: that hides the file
        // list, which is where the user would retry the skipped file from.
        await SettleInitialRefreshAsync();
        AddDeviceFiles("log_a.bin", "log_b.bin");

        SetupImportToFailOnly(new SdCardEmptyTransferException("log_a.bin"), "log_a.bin");

        // Act
        await _viewModel.ImportAllFilesCommand.ExecuteAsync(null);

        // Assert
        Assert.AreEqual(SdCardState.Ok, _viewModel.SdCardState);
        Assert.IsTrue(_viewModel.HasFiles);
        Assert.IsFalse(_viewModel.IsBusy);
    }

    [TestMethod]
    public async Task ImportAllFiles_WhenTheTransportTimesOut_KeepsGoingAndStaysOffTheErrorPath()
    {
        // Arrange — #779 and #780 together on the transport that actually ships. Core's plain
        // TimeoutException, normalised by the importer, must neither file a Sentry issue nor end
        // the batch.
        await SettleInitialRefreshAsync();
        AddDeviceFiles(THREE_FILES);

        SetupImportToFailOnly(QuickTransportStall("log_b.bin"), "log_b.bin");

        // Act
        await _viewModel.ImportAllFilesCommand.ExecuteAsync(null);

        // Assert
        Assert.AreEqual(3, ImportedFileNames().Count());
        Assert.AreEqual(2, _publishedSessions.Count);
        _mockLogger.Verify(l => l.Error(It.IsAny<Exception>(), It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public void BuildImportAllSummary_WhenFilesAreSkipped_ReportsThemAsSkippedNotAsACardWideAbort()
    {
        // Arrange — the completion dialog used to say "Import stopped early: power-cycle the
        // device" for a benign empty log, which is advice for a fault that is not there.
        var outcome = new ImportAllOutcome { TotalCount = 3, ImportedCount = 2 };
        outcome.RecordSkip("log_a.bin", SdCardFailureClassifier.EMPTY_TRANSFER_GUIDANCE);

        // Act
        var summary = DeviceLogsViewModel.BuildImportAllSummary(outcome);

        // Assert
        StringAssert.Contains(summary, "Imported 2 of 3 files.");
        StringAssert.Contains(summary, "Skipped 1 file(s)");
        StringAssert.Contains(summary, "log_a.bin",
            "Naming the skipped file is what lets the user retry it individually.");
        StringAssert.Contains(summary, SdCardFailureClassifier.EMPTY_TRANSFER_GUIDANCE);
        Assert.IsFalse(summary.Contains("Import stopped"),
            "Nothing stopped: the batch ran to the end, so the dialog must not claim otherwise.");
        Assert.IsFalse(summary.Contains(SdCardFailureClassifier.POWER_CYCLE_GUIDANCE),
            "Issue #780: a skipped file is not evidence the device needs a power cycle.");
    }

    [TestMethod]
    public void BuildImportAllSummary_WhenManyFilesAreSkipped_ListsAFewAndCountsTheRestOnce()
    {
        // Arrange — a card full of empty logs must not produce a dialog taller than the screen,
        // and repeating identical guidance per file is noise.
        var outcome = new ImportAllOutcome { TotalCount = 8, ImportedCount = 0 };
        for (var i = 0; i < 8; i++)
        {
            outcome.RecordSkip($"log_{i}.bin", SdCardFailureClassifier.EMPTY_TRANSFER_GUIDANCE);
        }

        // Act
        var summary = DeviceLogsViewModel.BuildImportAllSummary(outcome);

        // Assert
        StringAssert.Contains(summary, "log_4.bin");
        Assert.IsFalse(summary.Contains("log_5.bin"), "Only the first few names are listed.");
        StringAssert.Contains(summary, "...and 3 more");
        Assert.AreEqual(1, CountOccurrences(summary, SdCardFailureClassifier.EMPTY_TRANSFER_GUIDANCE),
            "Identical guidance must be stated once, not once per file.");
    }

    [TestMethod]
    public void BuildImportAllSummary_WhenTheCardIsUnavailable_SaysWhereItStoppedAndWhy()
    {
        // Arrange — the early abort is still reported, and still carries the power-cycle advice.
        var outcome = new ImportAllOutcome
        {
            TotalCount = 4,
            ImportedCount = 1,
            AbortedOnFile = "log_b.bin",
            AbortingFailure = SdCardFailureClassifier.Classify(
                new SdCardDownloadStalledException("log_b.bin", TimeSpan.FromSeconds(90)))
        };

        // Act
        var summary = DeviceLogsViewModel.BuildImportAllSummary(outcome);

        // Assert
        StringAssert.Contains(summary, "Import stopped at log_b.bin");
        StringAssert.Contains(summary, SdCardFailureClassifier.POWER_CYCLE_GUIDANCE);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
