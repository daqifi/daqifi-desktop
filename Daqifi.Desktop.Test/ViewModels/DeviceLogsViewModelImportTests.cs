using Daqifi.Core.Device.SdCard;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
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

    private Mock<IStreamingDevice> _mockDevice = null!;
    private Mock<ISdCardSessionImporter> _mockImporter = null!;
    private Mock<IAppLogger> _mockLogger = null!;
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

        // DeviceLogsViewModel's constructor reads the process-wide ConnectionManager.Instance
        // singleton; other test classes leave devices on it, so reset it for order-independence.
        ConnectionManager.Instance.ConnectedDevices.Clear();

        _viewModel = new DeviceLogsViewModel(_mockLogger.Object, _mockImporter.Object)
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
    public async Task ImportFile_EmptyTransfer_SurfacesPowerCycleGuidanceOnTheCardPanel()
    {
        // Arrange
        await SettleInitialRefreshAsync();
        SetupImportToThrow(new SdCardEmptyTransferException(FileName));

        // Act
        await ImportAsync();

        // Assert — the user gets an actionable state instead of a silent busy overlay.
        Assert.AreEqual(SdCardState.Error, _viewModel.SdCardState);
        Assert.IsTrue(_viewModel.HasSdCardError);
        Assert.AreEqual(SdCardFailureClassifier.POWER_CYCLE_GUIDANCE, _viewModel.SdCardErrorGuidance);
        Assert.IsFalse(string.IsNullOrEmpty(_viewModel.SdCardErrorMessage));
    }

    [TestMethod]
    public async Task ImportFile_DownloadStalled_LogsWarningAndReportsTheStall()
    {
        // Arrange — what the importer's stall watchdog raises when the device goes quiet.
        await SettleInitialRefreshAsync();
        SetupImportToThrow(new SdCardDownloadStalledException(FileName, TimeSpan.FromSeconds(90)));

        // Act
        await ImportAsync();

        // Assert
        _mockLogger.Verify(l => l.Warning(It.IsAny<Exception>(), It.IsAny<string>()), Times.Once);
        _mockLogger.Verify(l => l.Error(It.IsAny<Exception>(), It.IsAny<string>()), Times.Never);
        Assert.AreEqual(SdCardState.Error, _viewModel.SdCardState);
        Assert.AreEqual(SdCardFailureClassifier.POWER_CYCLE_GUIDANCE, _viewModel.SdCardErrorGuidance);
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
        // Arrange — three files on a device whose SD subsystem is wedged. Every download would
        // burn Core's full retry window (~50s observed on the bench), so the batch must give up
        // rather than make the user wait through all of them.
        await SettleInitialRefreshAsync();
        foreach (var name in new[] { "log_a.bin", "log_b.bin", "log_c.bin" })
        {
            _viewModel.DeviceFiles.Add(new SdCardFile { FileName = name });
        }

        SetupImportToThrow(new SdCardEmptyTransferException("log_a.bin"));

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
        foreach (var name in new[] { "log_a.bin", "log_b.bin", "log_c.bin" })
        {
            _viewModel.DeviceFiles.Add(new SdCardFile { FileName = name });
        }

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
}
