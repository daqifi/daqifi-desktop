using Daqifi.Core.Device.SdCard;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.Loggers;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Daqifi.Desktop.Test.Loggers;

/// <summary>
/// Covers how <see cref="SdCardSessionImporter"/> bounds and reports a download that the device
/// does not honour.
///
/// Core v1.4.0 bounds the download itself, but its budget is 30 minutes and
/// <c>DaqifiStreamingDevice.SdCardDownloadTimeout</c> is <c>internal virtual</c>, so the importer
/// keeps a shorter bound of its own. Issue #754 showed what an unbounded wait costs: an import sat
/// on a busy overlay for two minutes with no error. The importer's bound is on <i>silence</i>, not
/// on total elapsed time, so a large file that is downloading steadily is never cut off.
///
/// Everything the importer raises for a transfer that did not complete is Core's typed
/// <see cref="SdCardTransferStalledException"/> (daqifi-core#398 gap 1), so it reaches the
/// classifier's stall arm rather than the default Sentry arm — the #779 regression.
///
/// The empty-download cases cover issues #593 and daqifi-core#398 gap 2: a file the device listed
/// as 0 bytes is a legitimate empty log and must import rather than fail.
/// </summary>
[TestClass]
public class SdCardDownloadFailureTests
{
    private const string FileName = "log_20260623_143217.bin";

    // Long enough that scheduling jitter cannot trip it spuriously, short enough to keep the test
    // fast. The production value is SdCardSessionImporter.DOWNLOAD_STALL_TIMEOUT.
    private static readonly TimeSpan STALL_TIMEOUT = TimeSpan.FromMilliseconds(300);

    private Mock<IStreamingDevice> _mockDevice = null!;
    private SdCardSessionImporter _importer = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockDevice = new Mock<IStreamingDevice>();
        _mockDevice.Setup(d => d.DeviceSerialNo).Returns("DAQ-TEST-001");

        // The watchdog runs entirely before anything is written, so a stub context factory is
        // enough — these tests never reach the database.
        _importer = new SdCardSessionImporter(
            new Mock<IDbContextFactory<LoggingContext>>().Object, STALL_TIMEOUT);
    }

    [TestMethod]
    public async Task Download_WhenDeviceGoesSilent_ThrowsTimeout()
    {
        // Arrange — a device that accepts the request and then never answers. This is the failure
        // Core cannot detect for us, and the one that hung the UI in issue #754.
        _mockDevice
            .Setup(d => d.DownloadSdCardFileAsync(
                It.IsAny<string>(), It.IsAny<IProgress<SdCardTransferProgress>>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, IProgress<SdCardTransferProgress> _, CancellationToken ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return new SdCardDownloadResult(FileName, 0, TimeSpan.Zero, null);
            });

        // Act
        var ex = await Assert.ThrowsExactlyAsync<SdCardTransferStalledException>(() =>
            _importer.DownloadWithStallWatchdogAsync(_mockDevice.Object, FileName, CancellationToken.None));

        // Assert — the reason is what the failure classifier turns into "power-cycle the device"
        // and into abandoning the rest of a batch, so it is load-bearing, not incidental.
        StringAssert.Contains(ex.Message, FileName);
        Assert.AreEqual(SdCardTransferStallReason.TransferTimeout, ex.Reason,
            "The watchdog watched the device say nothing for its full window, which is what makes " +
            "this one broad enough to abandon a batch import over.");
        Assert.AreEqual(STALL_TIMEOUT, ex.Timeout);
    }

    [TestMethod]
    public async Task Download_WhenCoreStallsTheTransfer_ReachesTheCallerUntouched()
    {
        // Arrange — issue #779. Over USB serial the watchdog above never gets to fire: Core's
        // serial transport drops SerialPort.ReadTimeout to 500ms and hands the raw BaseStream to
        // SdCardFileReceiver, .NET's SerialStream returns 0 bytes on a read timeout instead of
        // throwing or honouring the token, and the receiver treats that as fatal. So a wedged
        // device produces this in about half a second, never the cancellation the watchdog converts.
        //
        // Core v1.4.0 types it (daqifi-core#398 gap 1), and it does not derive from
        // TimeoutException, so it must pass straight through the normalisation below rather than
        // being re-wrapped and losing its reason and byte count.
        var coreStall = new SdCardTransferStalledException(
            FileName, bytesReceived: 0, SdCardTransferStallReason.NoDataReceived);

        _mockDevice
            .Setup(d => d.DownloadSdCardFileAsync(
                It.IsAny<string>(), It.IsAny<IProgress<SdCardTransferProgress>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(coreStall);

        // Act
        var ex = await Assert.ThrowsExactlyAsync<SdCardTransferStalledException>(() =>
            _importer.DownloadWithStallWatchdogAsync(_mockDevice.Object, FileName, CancellationToken.None));

        // Assert
        Assert.AreSame(coreStall, ex, "Core already named this failure; re-wrapping it would only lose detail.");
        Assert.AreEqual(SdCardTransferStallReason.NoDataReceived, ex.Reason,
            "The transport gave up in well under a second, so this says far less about the card " +
            "than a transfer deadline does and must not abort a batch (issue #780).");
    }

    [TestMethod]
    public async Task Download_WhenCoreAbandonsOnItsHardDeadline_BecomesAStallRatherThanABareTimeout()
    {
        // Arrange — Core v1.4.0 still reports its own hard download deadline as a bare
        // TimeoutException: the worker was abandoned mid-transfer, so no receiver is left to name a
        // reason (daqifi-core#399/#401). Unnormalised it falls through to the classifier's default
        // arm — a Sentry issue plus "check the device connection", the exact #779 regression.
        var coreDeadline = new TimeoutException(
            "SD card download of 'x' did not complete within 1800s and was abandoned.");

        _mockDevice
            .Setup(d => d.DownloadSdCardFileAsync(
                It.IsAny<string>(), It.IsAny<IProgress<SdCardTransferProgress>>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, IProgress<SdCardTransferProgress> progress, CancellationToken ct) =>
            {
                // Keep the watchdog fed so it is Core's deadline, not the desktop's, that decides.
                for (var i = 1; i <= 4; i++)
                {
                    await Task.Delay(STALL_TIMEOUT / 4, ct);
                    progress?.Report(new SdCardTransferProgress(i * 1024L, FileName));
                }

                throw coreDeadline;
            });

        // Act
        var ex = await Assert.ThrowsExactlyAsync<SdCardTransferStalledException>(() =>
            _importer.DownloadWithStallWatchdogAsync(_mockDevice.Object, FileName, CancellationToken.None));

        // Assert
        StringAssert.Contains(ex.Message, FileName);
        Assert.AreEqual(SdCardTransferStallReason.TransferTimeout, ex.Reason);
        Assert.AreSame(coreDeadline, ex.InnerException,
            "Core's own message is the only diagnostic the log gets about how long it waited.");
        Assert.AreEqual(4096L, ex.BytesReceived,
            "How much of the file arrived is what separates a device that was delivering data from " +
            "one that never started.");
    }

    [TestMethod]
    public async Task Download_WhenTheCallerCancelsAndTheTransportTimesOut_StaysCancelled()
    {
        // Arrange — over serial the read does not observe the cancellation token, so pressing
        // cancel can surface as a transport timeout rather than a cancellation. Relabelling that
        // as a stall would report a device fault the user caused themselves, and would let a
        // batch import carry on past a cancel instead of stopping.
        using var cts = new CancellationTokenSource();
        _mockDevice
            .Setup(d => d.DownloadSdCardFileAsync(
                It.IsAny<string>(), It.IsAny<IProgress<SdCardTransferProgress>>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, IProgress<SdCardTransferProgress> _, CancellationToken _) =>
            {
                await cts.CancelAsync();
                throw new TimeoutException("Transport stream closed before receiving the EOF marker.");
            });

        // Act
        var download = () => _importer.DownloadWithStallWatchdogAsync(_mockDevice.Object, FileName, cts.Token);

        // Assert
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(download);
    }

    [TestMethod]
    public async Task Download_WhenTheDeviceFailsForANonTimeoutReason_IsLeftAlone()
    {
        // Arrange — regression guard on the scope of the #779 normalisation. Only timeouts out of
        // the download call become stalls; a typed device condition Core already names must reach
        // the classifier unchanged so it keeps its own state and guidance.
        _mockDevice
            .Setup(d => d.DownloadSdCardFileAsync(
                It.IsAny<string>(), It.IsAny<IProgress<SdCardTransferProgress>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SdCardNotPresentException(new List<string>(), "No card"));

        // Act
        var download = () => _importer.DownloadWithStallWatchdogAsync(
            _mockDevice.Object, FileName, CancellationToken.None);

        // Assert
        await Assert.ThrowsExactlyAsync<SdCardNotPresentException>(download);
    }

    [TestMethod]
    public async Task Download_WhileProgressKeepsArriving_IsNotCutOff()
    {
        // Arrange — a slow but healthy transfer: it runs for several stall windows, reporting a
        // chunk at a time. Regression guard against turning the watchdog into a wall-clock cap,
        // which would break exactly the large downloads it is supposed to protect.
        var totalTransferTime = STALL_TIMEOUT * 4;
        var chunkInterval = STALL_TIMEOUT / 4;
        var expected = new SdCardDownloadResult(FileName, 4096, totalTransferTime, "C:\\temp\\file.bin");

        _mockDevice
            .Setup(d => d.DownloadSdCardFileAsync(
                It.IsAny<string>(), It.IsAny<IProgress<SdCardTransferProgress>>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, IProgress<SdCardTransferProgress> progress, CancellationToken ct) =>
            {
                var chunks = (int)(totalTransferTime / chunkInterval);
                for (var i = 1; i <= chunks; i++)
                {
                    await Task.Delay(chunkInterval, ct);
                    progress?.Report(new SdCardTransferProgress(i * 1024L, FileName));
                }

                return expected;
            });

        // Act
        var result = await _importer.DownloadWithStallWatchdogAsync(
            _mockDevice.Object, FileName, CancellationToken.None);

        // Assert
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public async Task Import_WhenNoFileIsDelivered_KeepsTheErrorPathBecauseItIsAContractViolation()
    {
        // Arrange — the download reports success without producing a local file at all. Core's
        // temp-file overload always sets FilePath on a successful result, so this cannot come from
        // a device: it is a broken IStreamingDevice implementation and belongs on the Error/Sentry
        // path, NOT reported as the wedged-SD-subsystem condition it used to masquerade as.
        _mockDevice
            .Setup(d => d.DownloadSdCardFileAsync(
                It.IsAny<string>(), It.IsAny<IProgress<SdCardTransferProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SdCardDownloadResult(FileName, 0, TimeSpan.Zero, null));

        // Act & Assert
        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _importer.ImportFromDeviceAsync(_mockDevice.Object, FileName));
        StringAssert.Contains(ex.Message, FileName);
    }

    [TestMethod]
    public async Task Download_WhenCallerCancels_StaysCancelledRatherThanBecomingATimeout()
    {
        // Arrange — a caller-requested cancel must not be relabelled as a device timeout, or the
        // UI would tell the user to power-cycle a perfectly healthy device.
        using var cts = new CancellationTokenSource();
        _mockDevice
            .Setup(d => d.DownloadSdCardFileAsync(
                It.IsAny<string>(), It.IsAny<IProgress<SdCardTransferProgress>>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, IProgress<SdCardTransferProgress> _, CancellationToken ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return new SdCardDownloadResult(FileName, 0, TimeSpan.Zero, null);
            });

        // Act
        var downloadTask = _importer.DownloadWithStallWatchdogAsync(_mockDevice.Object, FileName, cts.Token);
        await cts.CancelAsync();

        // Assert
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => downloadTask);
    }
}
