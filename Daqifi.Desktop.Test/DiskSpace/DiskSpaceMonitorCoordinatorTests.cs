using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.DiskSpace;
using Moq;

namespace Daqifi.Desktop.Test.DiskSpace;

/// <summary>
/// Behavior contract for <see cref="DiskSpaceMonitorCoordinator"/>. The disk-space gate, the
/// in-session monitor delegation, and the low/critical event handling were extracted from
/// <c>DaqifiViewModel</c> (issue #592); these tests target the coordinator directly through a
/// lightweight <see cref="FakeDiskSpaceMonitorHost"/> and <see cref="FakeDiskSpaceMonitor"/>, with no
/// WPF dependency. The asserted behavior (block/warn verdicts, dialog text, auto-stop on critical,
/// suppress-initial-warning plumbing) is unchanged from the pre-refactor view model.
/// </summary>
[TestClass]
public class DiskSpaceMonitorCoordinatorTests
{
    private const long MB = 1024 * 1024;

    private FakeDiskSpaceMonitorHost _host = null!;
    private FakeDiskSpaceMonitor _monitor = null!;
    private DiskSpaceMonitorCoordinator _coordinator = null!;

    [TestInitialize]
    public void Setup()
    {
        _host = new FakeDiskSpaceMonitorHost();
        _monitor = new FakeDiskSpaceMonitor();
        _coordinator = new DiskSpaceMonitorCoordinator(_host, _monitor, Mock.Of<IAppLogger>());
    }

    #region EvaluateStartLogging Tests

    [TestMethod]
    public void EvaluateStartLogging_OkSpace_ReturnsAllowed_AndShowsNoDialog()
    {
        _monitor.NextCheckResult = new DiskSpaceCheckResult(1000 * MB, DiskSpaceLevel.Ok);

        var decision = _coordinator.EvaluateStartLogging();

        Assert.IsTrue(decision.CanStart);
        Assert.IsFalse(decision.SuppressInitialWarning);
        Assert.AreEqual(0, _host.Messages.Count);
        Assert.AreEqual(1, _monitor.CheckPreLoggingSpaceCallCount);
    }

    [TestMethod]
    public void EvaluateStartLogging_Critical_ReturnsBlocked_AndShowsBlockingDialog()
    {
        _monitor.NextCheckResult = new DiskSpaceCheckResult(30 * MB, DiskSpaceLevel.Critical);

        var decision = _coordinator.EvaluateStartLogging();

        Assert.IsFalse(decision.CanStart);
        Assert.IsFalse(decision.SuppressInitialWarning);
        Assert.AreEqual(1, _host.Messages.Count);
        Assert.AreEqual("Cannot Start Logging", _host.Messages[0].Title);
        StringAssert.Contains(_host.Messages[0].Message, "30 MB");
        StringAssert.Contains(_host.Messages[0].Message, "critically low");
        // A blocked start must not touch the monitor's start/stop.
        Assert.AreEqual(0, _monitor.StartMonitoringCallCount);
    }

    [TestMethod]
    public void EvaluateStartLogging_PreSessionWarning_ReturnsAllowedWithWarning_AndShowsWarningDialog()
    {
        _monitor.NextCheckResult = new DiskSpaceCheckResult(300 * MB, DiskSpaceLevel.PreSessionWarning);

        var decision = _coordinator.EvaluateStartLogging();

        Assert.IsTrue(decision.CanStart);
        Assert.IsTrue(decision.SuppressInitialWarning);
        Assert.AreEqual(1, _host.Messages.Count);
        Assert.AreEqual("Low Disk Space Warning", _host.Messages[0].Title);
        StringAssert.Contains(_host.Messages[0].Message, "300 MB");
        // The pre-logging warning wording differs from the in-session warning event.
        StringAssert.Contains(_host.Messages[0].Message, "may be stopped automatically if space runs out");
    }

    [TestMethod]
    public void EvaluateStartLogging_Warning_ReturnsAllowedWithWarning()
    {
        _monitor.NextCheckResult = new DiskSpaceCheckResult(80 * MB, DiskSpaceLevel.Warning);

        var decision = _coordinator.EvaluateStartLogging();

        Assert.IsTrue(decision.CanStart);
        Assert.IsTrue(decision.SuppressInitialWarning);
        Assert.AreEqual(1, _host.Messages.Count);
        Assert.AreEqual("Low Disk Space Warning", _host.Messages[0].Title);
    }

    #endregion

    #region StartMonitoring / StopMonitoring Tests

    [TestMethod]
    public void StartMonitoring_DelegatesToMonitor_PreservingSuppressFlag()
    {
        _coordinator.StartMonitoring(suppressInitialWarning: true);

        Assert.AreEqual(1, _monitor.StartMonitoringCallCount);
        Assert.AreEqual(true, _monitor.LastSuppressInitialWarning);
    }

    [TestMethod]
    public void StartMonitoring_FalseSuppressFlag_PassedThrough()
    {
        _coordinator.StartMonitoring(suppressInitialWarning: false);

        Assert.AreEqual(false, _monitor.LastSuppressInitialWarning);
    }

    [TestMethod]
    public void StopMonitoring_DelegatesToMonitor()
    {
        _coordinator.StopMonitoring();

        Assert.AreEqual(1, _monitor.StopMonitoringCallCount);
    }

    #endregion

    #region Event Handling Tests

    [TestMethod]
    public void LowSpaceWarning_ShowsWarningDialog_AndDoesNotStopLogging()
    {
        _monitor.RaiseLowSpaceWarning(80 * MB);

        Assert.AreEqual(0, _host.StopLoggingCallCount);
        Assert.AreEqual(1, _host.Messages.Count);
        Assert.AreEqual("Low Disk Space Warning", _host.Messages[0].Title);
        StringAssert.Contains(_host.Messages[0].Message, "80 MB");
        // In-session warning wording (distinct from the pre-logging gate warning).
        StringAssert.Contains(_host.Messages[0].Message, "will be stopped automatically if space drops below 50 MB");
    }

    [TestMethod]
    public void CriticalSpaceReached_StopsLogging_BeforeShowingDialog()
    {
        _monitor.RaiseCriticalSpaceReached(30 * MB);

        Assert.AreEqual(1, _host.StopLoggingCallCount);
        Assert.AreEqual(1, _host.Messages.Count);
        Assert.AreEqual("Logging Stopped — Disk Space Critical", _host.Messages[0].Title);
        StringAssert.Contains(_host.Messages[0].Message, "30 MB");
        // Logging must be stopped before the dialog is surfaced (preserves the original ordering).
        Assert.AreEqual("StopLogging", _host.CallLog[0]);
        Assert.AreEqual("Message:Logging Stopped — Disk Space Critical", _host.CallLog[1]);
    }

    #endregion

    #region Dispose Tests

    [TestMethod]
    public void Dispose_DisposesMonitor()
    {
        _coordinator.Dispose();

        Assert.IsTrue(_monitor.Disposed);
    }

    [TestMethod]
    public void Dispose_UnsubscribesFromMonitorEvents()
    {
        _coordinator.Dispose();

        _monitor.RaiseLowSpaceWarning(80 * MB);
        _monitor.RaiseCriticalSpaceReached(30 * MB);

        Assert.AreEqual(0, _host.Messages.Count);
        Assert.AreEqual(0, _host.StopLoggingCallCount);
    }

    [TestMethod]
    public void Dispose_CalledTwice_DisposesMonitorOnce()
    {
        _coordinator.Dispose();
        _coordinator.Dispose();

        Assert.AreEqual(1, _monitor.DisposeCallCount);
    }

    #endregion

    #region Constructor Validation Tests

    [TestMethod]
    public void Constructor_NullHost_ThrowsArgumentNullException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new DiskSpaceMonitorCoordinator(null!, _monitor, Mock.Of<IAppLogger>()));
    }

    [TestMethod]
    public void Constructor_NullMonitor_ThrowsArgumentNullException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new DiskSpaceMonitorCoordinator(_host, null!, Mock.Of<IAppLogger>()));
    }

    [TestMethod]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new DiskSpaceMonitorCoordinator(_host, _monitor, null!));
    }

    #endregion

    #region Fakes

    /// <summary>In-memory <see cref="IDiskSpaceMonitorHost"/> recording stop/dialog calls in order.</summary>
    private sealed class FakeDiskSpaceMonitorHost : IDiskSpaceMonitorHost
    {
        public int StopLoggingCallCount { get; private set; }
        public List<(string Title, string Message)> Messages { get; } = [];

        /// <summary>Ordered log of host calls so tests can assert stop-before-dialog sequencing.</summary>
        public List<string> CallLog { get; } = [];

        public void StopLogging()
        {
            StopLoggingCallCount++;
            CallLog.Add("StopLogging");
        }

        public Task ShowDiskSpaceMessageAsync(string title, string message)
        {
            Messages.Add((title, message));
            CallLog.Add($"Message:{title}");
            return Task.CompletedTask;
        }
    }

    /// <summary>In-memory <see cref="IDiskSpaceMonitor"/> with controllable check result and raisable events.</summary>
    private sealed class FakeDiskSpaceMonitor : IDiskSpaceMonitor
    {
        public event EventHandler<DiskSpaceEventArgs>? LowSpaceWarning;
        public event EventHandler<DiskSpaceEventArgs>? CriticalSpaceReached;

        public DiskSpaceCheckResult NextCheckResult { get; set; } = new(long.MaxValue, DiskSpaceLevel.Ok);
        public int CheckPreLoggingSpaceCallCount { get; private set; }
        public int StartMonitoringCallCount { get; private set; }
        public int StopMonitoringCallCount { get; private set; }
        public bool? LastSuppressInitialWarning { get; private set; }
        public int DisposeCallCount { get; private set; }
        public bool Disposed => DisposeCallCount > 0;
        public bool IsMonitoring { get; private set; }

        public DiskSpaceCheckResult CheckPreLoggingSpace()
        {
            CheckPreLoggingSpaceCallCount++;
            return NextCheckResult;
        }

        public void StartMonitoring(bool suppressInitialWarning = false)
        {
            StartMonitoringCallCount++;
            LastSuppressInitialWarning = suppressInitialWarning;
            IsMonitoring = true;
        }

        public void StopMonitoring()
        {
            StopMonitoringCallCount++;
            IsMonitoring = false;
        }

        public void Dispose() => DisposeCallCount++;

        public void RaiseLowSpaceWarning(long availableBytes) =>
            LowSpaceWarning?.Invoke(this, new DiskSpaceEventArgs(availableBytes, DiskSpaceLevel.Warning));

        public void RaiseCriticalSpaceReached(long availableBytes) =>
            CriticalSpaceReached?.Invoke(this, new DiskSpaceEventArgs(availableBytes, DiskSpaceLevel.Critical));
    }

    #endregion
}
