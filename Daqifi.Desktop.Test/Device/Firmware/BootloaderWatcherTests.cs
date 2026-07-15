using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device.Firmware;
using Moq;

namespace Daqifi.Desktop.Test.Device.Firmware;

/// <summary>
/// Verifies the app-global bootloader watcher discovers and holds EVERY sitting HID bootloader, drops one
/// when its device goes away, and coordinates a flash by releasing only the target while every other
/// bootloader stays held — the multi-device "grab and hold all" behavior.
/// </summary>
[TestClass]
public class BootloaderWatcherTests
{
    private FakeDiscovery _discovery = null!;
    private Dictionary<string, FakeHold> _createdHolds = null!;
    private HashSet<string> _failOpenPaths = null!;
    private Mock<IAppLogger> _logger = null!;

    private const string PathA = @"\\?\hid#vid_04d8&pid_003c#a";
    private const string PathB = @"\\?\hid#vid_04d8&pid_003c#b";

    [TestInitialize]
    public void Setup()
    {
        _discovery = new FakeDiscovery();
        _createdHolds = new Dictionary<string, FakeHold>(StringComparer.Ordinal);
        _failOpenPaths = new HashSet<string>(StringComparer.Ordinal);
        _logger = new Mock<IAppLogger>();
    }

    private BootloaderWatcher CreateWatcher() =>
        new(_discovery, HoldFactory, _logger.Object);

    private IBootloaderHoldService HoldFactory(string devicePath, string? deviceName)
    {
        var hold = new FakeHold(devicePath, deviceName) { WillHold = !_failOpenPaths.Contains(devicePath) };
        _createdHolds[devicePath] = hold;
        return hold;
    }

    [TestMethod]
    public async Task Start_HoldsEveryDiscoveredBootloader()
    {
        using var watcher = CreateWatcher();
        watcher.Start();

        _discovery.Raise(PathA, "DAQiFi Bootloader");
        _discovery.Raise(PathB, "DAQiFi Bootloader");

        await WaitUntilAsync(() => watcher.Bootloaders.Count == 2);

        Assert.AreEqual(2, watcher.Bootloaders.Count);
        Assert.IsTrue(_createdHolds[PathA].IsHolding);
        Assert.IsTrue(_createdHolds[PathB].IsHolding);
        Assert.IsTrue(_discovery.IsRunning, "Discovery should be running after Start.");
    }

    [TestMethod]
    public async Task Discover_WithDeviceName_UsesDeviceNameOverLocationKey()
    {
        using var watcher = CreateWatcher();
        watcher.Start();

        _discovery.Raise(PathA, "DAQiFi Bootloader", "Port_#0001.Hub_#0001");
        await WaitUntilAsync(() => watcher.Bootloaders.Count == 1);

        var bootloader = watcher.Bootloaders.Single();
        Assert.AreEqual("DAQiFi Bootloader", bootloader.DisplayName);
        Assert.AreEqual("Port_#0001.Hub_#0001", bootloader.LocationKey);
    }

    [TestMethod]
    public async Task Discover_NoDeviceNameWithLocationKey_LabelsByLocation()
    {
        using var watcher = CreateWatcher();
        watcher.Start();

        _discovery.Raise(PathA, deviceName: null, locationKey: "Port_#0001.Hub_#0001");
        await WaitUntilAsync(() => watcher.Bootloaders.Count == 1);

        var bootloader = watcher.Bootloaders.Single();
        Assert.AreEqual("Bootloader on USB port Port_#0001.Hub_#0001", bootloader.DisplayName);
        Assert.AreEqual("Port_#0001.Hub_#0001", bootloader.LocationKey);
    }

    [TestMethod]
    public async Task Discover_NoDeviceNameNoLocationKey_UsesGenericFallback()
    {
        using var watcher = CreateWatcher();
        watcher.Start();

        _discovery.Raise(PathA, deviceName: null, locationKey: null);
        await WaitUntilAsync(() => watcher.Bootloaders.Count == 1);

        var bootloader = watcher.Bootloaders.Single();
        Assert.AreEqual("DAQiFi Bootloader", bootloader.DisplayName);
        Assert.IsNull(bootloader.LocationKey);
    }

    [TestMethod]
    public async Task Discover_SamePathTwice_HoldsOnce()
    {
        using var watcher = CreateWatcher();
        watcher.Start();

        _discovery.Raise(PathA, "DAQiFi Bootloader");
        await WaitUntilAsync(() => watcher.Bootloaders.Count == 1);
        _discovery.Raise(PathA, "DAQiFi Bootloader");
        await Task.Delay(50);

        Assert.AreEqual(1, watcher.Bootloaders.Count, "A duplicate discovery for the same path must not double-hold.");
        Assert.AreEqual(1, _createdHolds.Count);
    }

    [TestMethod]
    public async Task Discover_WhenOpenFails_NotListed()
    {
        // A just-flashed device in application mode: its open fails, so it must not appear in the list.
        _failOpenPaths.Add(PathA);
        using var watcher = CreateWatcher();
        watcher.Start();

        _discovery.Raise(PathA, "DAQiFi Bootloader");
        await Task.Delay(50);

        Assert.IsFalse(watcher.Bootloaders.Any(b => b.DevicePath == PathA),
            "A bootloader whose hold could not be opened must not be listed.");
        Assert.IsTrue(_createdHolds[PathA].Disposed, "An unheld hold must be disposed, not leaked.");
    }

    [TestMethod]
    public async Task HoldDropped_RemovesFromList_AndRaisesWatcherEvent()
    {
        using var watcher = CreateWatcher();
        string? droppedPath = null;
        watcher.HoldDropped += (_, e) => droppedPath = e.DevicePath;
        watcher.Start();

        _discovery.Raise(PathA, "DAQiFi Bootloader");
        await WaitUntilAsync(() => watcher.Bootloaders.Count == 1);

        _createdHolds[PathA].RaiseDropped();

        await WaitUntilAsync(() => watcher.Bootloaders.Count == 0);
        Assert.AreEqual(0, watcher.Bootloaders.Count);
        Assert.AreEqual(PathA, droppedPath, "The watcher must surface a HoldDropped for the removed device.");
        Assert.IsTrue(_createdHolds[PathA].Disposed, "A dropped hold must be disposed.");
    }

    [TestMethod]
    public async Task PrepareFlash_ReleasesOnlyTarget_PausesDiscovery_OthersStayHeld()
    {
        using var watcher = CreateWatcher();
        watcher.Start();
        _discovery.Raise(PathA, "DAQiFi Bootloader");
        _discovery.Raise(PathB, "DAQiFi Bootloader");
        await WaitUntilAsync(() => watcher.Bootloaders.Count == 2);

        var lease = await watcher.PrepareFlashAsync(PathA);

        Assert.AreEqual(1, _createdHolds[PathA].ReleaseCount, "The flash target's hold must be released.");
        Assert.AreEqual(0, _createdHolds[PathB].ReleaseCount, "Other bootloaders must stay held during the flash.");
        Assert.IsTrue(_createdHolds[PathB].IsHolding, "Other bootloaders must remain wedge-proof during the flash.");
        Assert.IsFalse(_discovery.IsRunning, "Discovery must be paused during a flash.");
        Assert.AreEqual(2, watcher.Bootloaders.Count, "The target stays listed (as the device being flashed).");

        // Failed/cancelled flash: device is still a bootloader → re-grabbed, discovery resumes.
        await lease.DisposeAsync();

        Assert.AreEqual(2, _createdHolds[PathA].BeginHoldCount, "Disposing the lease must re-grab the target.");
        Assert.IsTrue(_createdHolds[PathA].IsHolding);
        Assert.IsTrue(_discovery.IsRunning, "Discovery must resume after the flash lease is disposed.");
        Assert.AreEqual(2, watcher.Bootloaders.Count);
    }

    [TestMethod]
    public async Task PrepareFlash_AfterSuccessfulFlash_DropsTargetFromList()
    {
        using var watcher = CreateWatcher();
        watcher.Start();
        _discovery.Raise(PathA, "DAQiFi Bootloader");
        await WaitUntilAsync(() => watcher.Bootloaders.Count == 1);

        var lease = await watcher.PrepareFlashAsync(PathA);

        // A successful flash leaves the device in application mode: the re-grab open fails.
        _createdHolds[PathA].WillHold = false;
        await lease.DisposeAsync();

        Assert.AreEqual(0, watcher.Bootloaders.Count,
            "A successfully-flashed device is no longer a bootloader and must drop off the list.");
        Assert.IsTrue(_createdHolds[PathA].Disposed);
    }

    [TestMethod]
    public async Task SuspendDiscovery_PausesDiscovery_KeepsHolds_SuppressesNewGrabs()
    {
        using var watcher = CreateWatcher();
        watcher.Start();
        _discovery.Raise(PathA, "DAQiFi Bootloader");
        await WaitUntilAsync(() => watcher.Bootloaders.Count == 1);

        var lease = await watcher.SuspendDiscoveryAsync();

        Assert.IsFalse(_discovery.IsRunning, "Discovery must be paused while suspended.");
        Assert.IsTrue(_createdHolds[PathA].IsHolding, "Existing holds must stay alive during an auto-update suspend.");
        Assert.AreEqual(1, watcher.Bootloaders.Count);

        // A device appearing while suspended (e.g. the coordinator's own device) must NOT be grabbed.
        _discovery.Raise(PathB, "DAQiFi Bootloader");
        await Task.Delay(50);
        Assert.IsFalse(watcher.Bootloaders.Any(b => b.DevicePath == PathB),
            "New bootloaders must not be grabbed while discovery is suspended.");

        await lease.DisposeAsync();
        Assert.IsTrue(_discovery.IsRunning, "Discovery must resume after the suspend lease is disposed.");
    }

    [TestMethod]
    public async Task OverlappingFlashAndSuspend_DiscoveryStaysPausedUntilBothComplete()
    {
        using var watcher = CreateWatcher();
        watcher.Start();
        _discovery.Raise(PathA, "DAQiFi Bootloader");
        await WaitUntilAsync(() => watcher.Bootloaders.Count == 1);

        // A manual flash (PrepareFlashAsync → _flashingPath) and an auto-update (SuspendDiscoveryAsync →
        // _grabSuppressed) overlap on a multi-device bench.
        var flashLease = await watcher.PrepareFlashAsync(PathA);
        var suspendLease = await watcher.SuspendDiscoveryAsync();
        Assert.IsFalse(_discovery.IsRunning, "Discovery paused once either operation starts.");

        // The auto-update finishes first — discovery must NOT resume while the manual flash is still live.
        await suspendLease.DisposeAsync();
        Assert.IsFalse(_discovery.IsRunning,
            "Discovery must stay paused while the manual flash is still in progress.");

        // The manual flash finishes — now it is safe to resume.
        await flashLease.DisposeAsync();
        Assert.IsTrue(_discovery.IsRunning, "Discovery must resume only once both operations complete.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }
    }

    #region Fakes
    private sealed class FakeDiscovery : IBootloaderDiscovery
    {
        public event EventHandler<BootloaderDiscoveredEventArgs>? BootloaderDiscovered;
        public bool IsRunning { get; private set; }

        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;

        // Simulate a discovery cycle surfacing a device — fired regardless of running state so tests can
        // also exercise the "suppressed while paused" guard.
        public void Raise(string devicePath, string? deviceName, string? locationKey = null) =>
            BootloaderDiscovered?.Invoke(this, new BootloaderDiscoveredEventArgs(devicePath, deviceName, locationKey));
    }

    private sealed class FakeHold : IBootloaderHoldService
    {
        public FakeHold(string devicePath, string? deviceName)
        {
            DevicePath = devicePath;
            DeviceName = deviceName;
        }

        public bool IsHolding { get; private set; }
        public string? DevicePath { get; }
        public string? DeviceName { get; }

        /// <summary>Result the next <see cref="BeginHoldAsync"/> sets <see cref="IsHolding"/> to.</summary>
        public bool WillHold { get; set; }
        public int BeginHoldCount { get; private set; }
        public int ReleaseCount { get; private set; }
        public bool Disposed { get; private set; }

        public event EventHandler? HoldDropped;

        public Task BeginHoldAsync(CancellationToken cancellationToken = default)
        {
            BeginHoldCount++;
            IsHolding = WillHold;
            return Task.CompletedTask;
        }

        public Task PauseForFlashAsync()
        {
            IsHolding = false;
            return Task.CompletedTask;
        }

        public Task ReleaseAsync()
        {
            ReleaseCount++;
            IsHolding = false;
            return Task.CompletedTask;
        }

        public void RaiseDropped() => HoldDropped?.Invoke(this, EventArgs.Empty);

        public void Dispose() => Disposed = true;
    }
    #endregion
}
