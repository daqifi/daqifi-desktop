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
/// The stall watchdog exists because neither public <c>DaqifiStreamingDevice.DownloadSdCardFileAsync</c>
/// overload in Daqifi.Core 1.3.0 exposes the timeout its underlying <c>SdCardFileReceiver.ReceiveAsync</c>
/// accepts, so the desktop cannot bound an SD download through Core. Issue #754 showed what that
/// costs: an import sat on a busy overlay for two minutes with no error. Until Core exposes the
/// knob, the importer bounds the wait itself — but on <i>silence</i>, not on total elapsed time,
/// so a large file that is downloading steadily is never cut off.
///
/// The empty-download cases cover issue #593: the importer's own "0 bytes" guard is the same
/// wedged-SD-subsystem condition, and used to escape as a bare <see cref="InvalidOperationException"/>
/// that the Error path filed to Sentry.
/// </summary>
[TestClass]
public class SdCardDownloadFailureTests
{
    private const string FileName = "log_20260623_143217.bin";

    // Long enough that scheduling jitter cannot trip it spuriously, short enough to keep the test
    // fast. The production value is SdCardSessionImporter.DOWNLOAD_STALL_TIMEOUT.
    private static readonly TimeSpan StallTimeout = TimeSpan.FromMilliseconds(300);

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
            new Mock<IDbContextFactory<LoggingContext>>().Object, StallTimeout);
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
        var ex = await Assert.ThrowsExactlyAsync<SdCardDownloadStalledException>(() =>
            _importer.DownloadWithStallWatchdogAsync(_mockDevice.Object, FileName, CancellationToken.None));

        // Assert — this type is what the failure classifier turns into "power-cycle the device",
        // so it is load-bearing, not incidental. It derives from TimeoutException so callers that
        // only care that the operation timed out still catch it.
        StringAssert.Contains(ex.Message, FileName);
        Assert.IsInstanceOfType<TimeoutException>(ex);
    }

    [TestMethod]
    public async Task Download_WhileProgressKeepsArriving_IsNotCutOff()
    {
        // Arrange — a slow but healthy transfer: it runs for several stall windows, reporting a
        // chunk at a time. Regression guard against turning the watchdog into a wall-clock cap,
        // which would break exactly the large downloads it is supposed to protect.
        var totalTransferTime = StallTimeout * 4;
        var chunkInterval = StallTimeout / 4;
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
    public async Task Import_WhenTheDownloadedFileIsEmpty_ReportsTheWedgedSubsystemCondition()
    {
        // Arrange — the device lists the file and "downloads" it, but nothing lands on disk. This
        // is issue #593: previously an InvalidOperationException, which the ViewModel could only
        // treat as an app fault and file to Sentry.
        var tempPath = Path.Combine(Path.GetTempPath(), $"daqifi_test_{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(tempPath, []);

        try
        {
            _mockDevice
                .Setup(d => d.DownloadSdCardFileAsync(
                    It.IsAny<string>(), It.IsAny<IProgress<SdCardTransferProgress>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SdCardDownloadResult(FileName, 0, TimeSpan.Zero, tempPath));

            // Act & Assert — the typed exception is what routes this to Warning plus
            // "power-cycle the device" instead of the Error path.
            await Assert.ThrowsExactlyAsync<SdCardEmptyTransferException>(() =>
                _importer.ImportFromDeviceAsync(_mockDevice.Object, FileName));
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [TestMethod]
    public async Task Import_WhenNoFileIsDelivered_ReportsTheWedgedSubsystemCondition()
    {
        // Arrange — the download "succeeds" without producing a local file at all.
        _mockDevice
            .Setup(d => d.DownloadSdCardFileAsync(
                It.IsAny<string>(), It.IsAny<IProgress<SdCardTransferProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SdCardDownloadResult(FileName, 0, TimeSpan.Zero, null));

        // Act & Assert
        await Assert.ThrowsExactlyAsync<SdCardEmptyTransferException>(() =>
            _importer.ImportFromDeviceAsync(_mockDevice.Object, FileName));
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
