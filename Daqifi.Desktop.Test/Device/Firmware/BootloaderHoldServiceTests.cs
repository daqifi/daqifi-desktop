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
    private int _connectCount;

    [TestInitialize]
    public void Setup()
    {
        _transport = new Mock<IHidTransport>();
        _logger = new Mock<IAppLogger>();
        _readCount = 0;
        _connectCount = 0;

        _transport
            .Setup(t => t.ConnectAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Interlocked.Increment(ref _connectCount); return Task.CompletedTask; });
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
    public async Task BeginHoldAsync_WithDevicePath_ConnectsByThatExactPath_NotFirstMatch()
    {
        const string path = @"\\?\hid#vid_04d8&pid_003c#6&abc&0&0000#{4d1e55b2}";
        _transport
            .Setup(t => t.ConnectByPathAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Interlocked.Increment(ref _connectCount); return Task.CompletedTask; });

        using var service = new BootloaderHoldService(
            _transport.Object, _logger.Object, TimeSpan.FromMilliseconds(50), devicePath: path, deviceName: "DAQiFi Bootloader");

        await service.BeginHoldAsync();

        Assert.IsTrue(service.IsHolding, "Service should report holding after a successful path-based open.");
        Assert.AreEqual(path, service.DevicePath);
        Assert.AreEqual("DAQiFi Bootloader", service.DeviceName);
        _transport.Verify(t => t.ConnectByPathAsync(path, It.IsAny<CancellationToken>()), Times.Once);
        // A path-targeted hold must address that exact device — never fall back to VID/PID first-match
        // (which could grab the wrong one of several identical bootloaders).
        _transport.Verify(t => t.ConnectAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);

        await service.ReleaseAsync();
    }

    [TestMethod]
    public async Task KeepAliveFault_RaisesHoldDropped()
    {
        // A non-timeout read error means the device went away under us → the watcher must be told.
        _transport
            .Setup(t => t.ReadAsync(It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .Returns((TimeSpan? _, CancellationToken ct) => KeepAliveReadThrows(ct));

        using var service = CreateService();
        var dropped = new TaskCompletionSource();
        service.HoldDropped += (_, _) => dropped.TrySetResult();

        await service.BeginHoldAsync();

        var fired = await Task.WhenAny(dropped.Task, Task.Delay(TimeSpan.FromSeconds(2))) == dropped.Task;
        Assert.IsTrue(fired, "HoldDropped must fire when the keep-alive read faults (device removed).");
    }

    [TestMethod]
    public async Task Dispose_DisposesOwnedTransport()
    {
        var service = CreateService();
        await service.BeginHoldAsync();

        service.Dispose();

        // The hold now owns a fresh per-device transport (the watcher news one up per device), so it must
        // dispose it to close the exclusive HID handle — otherwise a re-appearing bootloader at the same
        // path can be locked out until GC finalization.
        _transport.Verify(t => t.Dispose(), Times.AtLeastOnce);
    }

    [TestMethod]
    public async Task ReleaseAsync_DoesNotRaiseHoldDropped()
    {
        using var service = CreateService();
        var droppedCount = 0;
        service.HoldDropped += (_, _) => Interlocked.Increment(ref droppedCount);

        await service.BeginHoldAsync();
        await WaitUntilAsync(() => Volatile.Read(ref _readCount) >= 1, TimeSpan.FromSeconds(2));

        await service.ReleaseAsync();

        await Task.Delay(100);
        Assert.AreEqual(0, Volatile.Read(ref droppedCount),
            "A graceful release (not a device drop) must not raise HoldDropped.");
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
        await service.BeginHoldAsync(); // open #1; the keep-alive immediately faults and the loop exits

        // Re-grab must re-establish once the faulted loop has exited. Retry on a bounded deadline rather
        // than a fixed sleep so the test isn't timing-flaky: each BeginHoldAsync no-ops while the loop
        // task is still running, then re-opens the transport once it has completed.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (Volatile.Read(ref _connectCount) < 2 && DateTime.UtcNow < deadline)
        {
            await service.BeginHoldAsync();
            await Task.Delay(20);
        }

        Assert.IsTrue(Volatile.Read(ref _connectCount) >= 2,
            "A faulted keep-alive must not leave a stale hold; BeginHoldAsync should re-open the transport.");

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
