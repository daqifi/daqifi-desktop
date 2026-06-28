using System.IO;
using Daqifi.Core.Communication.Transport;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device.Firmware;
using Moq;

namespace Daqifi.Desktop.Test.Device.Firmware;

/// <summary>
/// Verifies the bootloader hold service grabs a sitting HID bootloader, keeps an interrupt-IN read
/// continuously pending (the keep-alive that prevents the #568 USB selective-suspend wedge), and hands
/// the handle off to the flasher without disconnecting — versus releasing it outright on close.
/// </summary>
[TestClass]
public class BootloaderHoldServiceTests
{
    private Mock<IHidTransport> _transport = null!;
    private Mock<IAppLogger> _logger = null!;
    private int _readCount;

    [TestInitialize]
    public void Setup()
    {
        _transport = new Mock<IHidTransport>();
        _logger = new Mock<IAppLogger>();
        _readCount = 0;

        _transport
            .Setup(t => t.ConnectAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _transport
            .Setup(t => t.DisconnectAsync())
            .Returns(Task.CompletedTask);
        // A sitting bootloader sends nothing, so every keep-alive read times out. Delay a touch so the
        // loop is observable (and so cancellation can interrupt an in-flight read for the hard-stop path).
        _transport
            .Setup(t => t.ReadAsync(It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .Returns((TimeSpan? _, CancellationToken ct) => KeepAliveReadStub(ct));
    }

    private async Task<byte[]> KeepAliveReadStub(CancellationToken ct)
    {
        Interlocked.Increment(ref _readCount);
        await Task.Delay(15, ct).ConfigureAwait(false);
        throw new TimeoutException();
    }

    private async Task<byte[]> KeepAliveReadThrows(CancellationToken ct)
    {
        Interlocked.Increment(ref _readCount);
        await Task.Yield();
        throw new IOException("device gone");
    }

    private BootloaderHoldService CreateService() =>
        new(_transport.Object, _logger.Object, TimeSpan.FromMilliseconds(50));

    [TestMethod]
    public async Task BeginHoldAsync_OpensTransport_AndKeepsReadingPending()
    {
        using var service = CreateService();

        await service.BeginHoldAsync();

        Assert.IsTrue(service.IsHolding, "Service should report holding after a successful open.");
        _transport.Verify(t => t.ConnectAsync(
            0x04D8, 0x003C, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);

        // The keep-alive loop must keep re-issuing reads (a single pending read is what keeps the USB
        // link out of selective-suspend).
        var sawMultipleReads = await WaitUntilAsync(() => Volatile.Read(ref _readCount) >= 2, TimeSpan.FromSeconds(2));
        Assert.IsTrue(sawMultipleReads, "Keep-alive loop should issue repeated reads while holding.");

        await service.ReleaseAsync();
    }

    [TestMethod]
    public async Task BeginHoldAsync_WhenNoBootloaderPresent_DoesNotThrow_AndDoesNotHold()
    {
        _transport
            .Setup(t => t.ConnectAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("No HID device found."));

        using var service = CreateService();

        // A just-flashed device is in application mode — the open fails and the hold is a no-op.
        await service.BeginHoldAsync();

        Assert.IsFalse(service.IsHolding, "Service must not report holding when the open failed.");
        _transport.Verify(t => t.ReadAsync(It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task PauseForFlashAsync_StopsKeepAlive_ButLeavesHandleOpen()
    {
        using var service = CreateService();
        await service.BeginHoldAsync();
        await WaitUntilAsync(() => Volatile.Read(ref _readCount) >= 1, TimeSpan.FromSeconds(2));

        await service.PauseForFlashAsync();

        Assert.IsFalse(service.IsHolding, "Service should no longer report holding after a pause.");
        // The whole point of pausing (vs releasing) is to hand the flasher a warm, still-open handle.
        _transport.Verify(t => t.DisconnectAsync(), Times.Never);

        // No further reads after the pause drains the loop.
        var countAfterPause = Volatile.Read(ref _readCount);
        await Task.Delay(100);
        Assert.AreEqual(countAfterPause, Volatile.Read(ref _readCount),
            "Keep-alive reads must stop once the hold is paused for flashing.");
    }

    [TestMethod]
    public async Task ReleaseAsync_StopsKeepAlive_AndDisconnects()
    {
        using var service = CreateService();
        await service.BeginHoldAsync();
        await WaitUntilAsync(() => Volatile.Read(ref _readCount) >= 1, TimeSpan.FromSeconds(2));

        await service.ReleaseAsync();

        Assert.IsFalse(service.IsHolding, "Service should no longer report holding after release.");
        _transport.Verify(t => t.DisconnectAsync(), Times.Once);

        var countAfterRelease = Volatile.Read(ref _readCount);
        await Task.Delay(100);
        Assert.AreEqual(countAfterRelease, Volatile.Read(ref _readCount),
            "Keep-alive reads must stop once the hold is released.");
    }

    [TestMethod]
    public async Task BeginHoldAsync_WhenAlreadyHolding_DoesNotReopen()
    {
        using var service = CreateService();

        await service.BeginHoldAsync();
        await service.BeginHoldAsync();

        _transport.Verify(t => t.ConnectAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);

        await service.ReleaseAsync();
    }

    [TestMethod]
    public async Task BeginHoldAsync_AfterKeepAliveFaults_ReEstablishesHold()
    {
        // Keep-alive read throws a non-timeout error → the loop exits (device I/O error / surprise removal).
        _transport
            .Setup(t => t.ReadAsync(It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .Returns((TimeSpan? _, CancellationToken ct) => KeepAliveReadThrows(ct));

        using var service = CreateService();
        await service.BeginHoldAsync();

        // Wait for the keep-alive loop to fault and exit (read attempted, then the loop task completes).
        await WaitUntilAsync(() => Volatile.Read(ref _readCount) >= 1, TimeSpan.FromSeconds(2));
        await Task.Delay(50);

        // Re-grab must NOT no-op on the stale holding state — it must tear down and reconnect.
        await service.BeginHoldAsync();

        _transport.Verify(t => t.ConnectAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        await service.ReleaseAsync();
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(10);
        }

        return condition();
    }
}
