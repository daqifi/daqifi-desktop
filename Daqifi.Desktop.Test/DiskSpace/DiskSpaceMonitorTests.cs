using Daqifi.Desktop.DiskSpace;

namespace Daqifi.Desktop.Test.DiskSpace;

[TestClass]
public class DiskSpaceMonitorTests
{
    #region Constants for readability
    private const long MB = 1024 * 1024;
    private const string TEST_PATH = @"C:\TestData";
    #endregion

    #region ClassifyLevel Tests

    [TestMethod]
    public void ClassifyLevel_Above500MB_PreSession_ReturnsOk()
    {
        var result = DiskSpaceMonitor.ClassifyLevel(600 * MB, preSession: true);
        Assert.AreEqual(DiskSpaceLevel.Ok, result);
    }

    [TestMethod]
    public void ClassifyLevel_Below500MB_PreSession_ReturnsPreSessionWarning()
    {
        var result = DiskSpaceMonitor.ClassifyLevel(400 * MB, preSession: true);
        Assert.AreEqual(DiskSpaceLevel.PreSessionWarning, result);
    }

    [TestMethod]
    public void ClassifyLevel_Below500MB_NotPreSession_ReturnsOk()
    {
        var result = DiskSpaceMonitor.ClassifyLevel(400 * MB, preSession: false);
        Assert.AreEqual(DiskSpaceLevel.Ok, result);
    }

    [TestMethod]
    public void ClassifyLevel_Below100MB_ReturnsWarning()
    {
        var result = DiskSpaceMonitor.ClassifyLevel(80 * MB, preSession: false);
        Assert.AreEqual(DiskSpaceLevel.Warning, result);
    }

    [TestMethod]
    public void ClassifyLevel_Below100MB_PreSession_ReturnsWarning()
    {
        var result = DiskSpaceMonitor.ClassifyLevel(80 * MB, preSession: true);
        Assert.AreEqual(DiskSpaceLevel.Warning, result);
    }

    [TestMethod]
    public void ClassifyLevel_Below50MB_ReturnsCritical()
    {
        var result = DiskSpaceMonitor.ClassifyLevel(30 * MB, preSession: false);
        Assert.AreEqual(DiskSpaceLevel.Critical, result);
    }

    [TestMethod]
    public void ClassifyLevel_Below50MB_PreSession_ReturnsCritical()
    {
        var result = DiskSpaceMonitor.ClassifyLevel(30 * MB, preSession: true);
        Assert.AreEqual(DiskSpaceLevel.Critical, result);
    }

    [TestMethod]
    public void ClassifyLevel_Exactly500MB_PreSession_ReturnsOk()
    {
        var result = DiskSpaceMonitor.ClassifyLevel(500 * MB, preSession: true);
        Assert.AreEqual(DiskSpaceLevel.Ok, result);
    }

    [TestMethod]
    public void ClassifyLevel_Exactly100MB_ReturnsOk()
    {
        var result = DiskSpaceMonitor.ClassifyLevel(100 * MB, preSession: false);
        Assert.AreEqual(DiskSpaceLevel.Ok, result);
    }

    [TestMethod]
    public void ClassifyLevel_Exactly50MB_ReturnsWarning()
    {
        var result = DiskSpaceMonitor.ClassifyLevel(50 * MB, preSession: false);
        Assert.AreEqual(DiskSpaceLevel.Warning, result);
    }

    [TestMethod]
    public void ClassifyLevel_ZeroBytes_ReturnsCritical()
    {
        var result = DiskSpaceMonitor.ClassifyLevel(0, preSession: false);
        Assert.AreEqual(DiskSpaceLevel.Critical, result);
    }

    #endregion

    #region CheckPreLoggingSpace Tests

    [TestMethod]
    public void CheckPreLoggingSpace_PlentyOfSpace_ReturnsOk()
    {
        var monitor = new DiskSpaceMonitor(TEST_PATH, _ => 1000 * MB);

        var result = monitor.CheckPreLoggingSpace();

        Assert.AreEqual(DiskSpaceLevel.Ok, result.Level);
        Assert.AreEqual(1000, result.AvailableMegabytes);
    }

    [TestMethod]
    public void CheckPreLoggingSpace_Below500MB_ReturnsPreSessionWarning()
    {
        var monitor = new DiskSpaceMonitor(TEST_PATH, _ => 300 * MB);

        var result = monitor.CheckPreLoggingSpace();

        Assert.AreEqual(DiskSpaceLevel.PreSessionWarning, result.Level);
        Assert.AreEqual(300, result.AvailableMegabytes);
    }

    [TestMethod]
    public void CheckPreLoggingSpace_Below100MB_ReturnsWarning()
    {
        var monitor = new DiskSpaceMonitor(TEST_PATH, _ => 80 * MB);

        var result = monitor.CheckPreLoggingSpace();

        Assert.AreEqual(DiskSpaceLevel.Warning, result.Level);
    }

    [TestMethod]
    public void CheckPreLoggingSpace_Below50MB_ReturnsCritical()
    {
        var monitor = new DiskSpaceMonitor(TEST_PATH, _ => 30 * MB);

        var result = monitor.CheckPreLoggingSpace();

        Assert.AreEqual(DiskSpaceLevel.Critical, result.Level);
    }

    #endregion

    #region StartMonitoring / StopMonitoring Tests

    [TestMethod]
    public void StartMonitoring_SetsIsMonitoringTrue()
    {
        var monitor = new DiskSpaceMonitor(TEST_PATH, _ => 1000 * MB);

        monitor.StartMonitoring();

        Assert.IsTrue(monitor.IsMonitoring);
        monitor.Dispose();
    }

    [TestMethod]
    public void StopMonitoring_SetsIsMonitoringFalse()
    {
        var monitor = new DiskSpaceMonitor(TEST_PATH, _ => 1000 * MB);
        monitor.StartMonitoring();

        monitor.StopMonitoring();

        Assert.IsFalse(monitor.IsMonitoring);
    }

    [TestMethod]
    public void StartMonitoring_CalledTwice_DoesNotThrow()
    {
        var monitor = new DiskSpaceMonitor(TEST_PATH, _ => 1000 * MB);

        monitor.StartMonitoring();
        monitor.StartMonitoring();

        Assert.IsTrue(monitor.IsMonitoring);
        monitor.Dispose();
    }

    [TestMethod]
    public void StopMonitoring_WhenNotStarted_DoesNotThrow()
    {
        var monitor = new DiskSpaceMonitor(TEST_PATH, _ => 1000 * MB);

        monitor.StopMonitoring();

        Assert.IsFalse(monitor.IsMonitoring);
    }

    [TestMethod]
    public void Dispose_StopsMonitoring()
    {
        var monitor = new DiskSpaceMonitor(TEST_PATH, _ => 1000 * MB);
        monitor.StartMonitoring();

        monitor.Dispose();

        Assert.IsFalse(monitor.IsMonitoring);
    }

    #endregion

    #region Event Tests

    [TestMethod]
    public void Monitoring_CriticalSpace_RaisesCriticalEvent()
    {
        var criticalRaised = false;
        var monitor = new DiskSpaceMonitor(TEST_PATH, _ => 30 * MB);
        monitor.CriticalSpaceReached += (_, e) =>
        {
            criticalRaised = true;
            Assert.AreEqual(DiskSpaceLevel.Critical, e.Level);
            Assert.AreEqual(30, e.AvailableMegabytes);
        };

        monitor.StartMonitoring();
        Thread.Sleep(500);
        monitor.Dispose();

        Assert.IsTrue(criticalRaised, "CriticalSpaceReached event should have been raised");
    }

    [TestMethod]
    public void Monitoring_WarningSpace_RaisesWarningEvent()
    {
        var warningRaised = false;
        var monitor = new DiskSpaceMonitor(TEST_PATH, _ => 80 * MB);
        monitor.LowSpaceWarning += (_, e) =>
        {
            warningRaised = true;
            Assert.AreEqual(DiskSpaceLevel.Warning, e.Level);
        };

        monitor.StartMonitoring();
        Thread.Sleep(500);
        monitor.Dispose();

        Assert.IsTrue(warningRaised, "LowSpaceWarning event should have been raised");
    }

    [TestMethod]
    public void Monitoring_OkSpace_RaisesNoEvents()
    {
        var eventRaised = false;
        var monitor = new DiskSpaceMonitor(TEST_PATH, _ => 1000 * MB);
        monitor.LowSpaceWarning += (_, _) => eventRaised = true;
        monitor.CriticalSpaceReached += (_, _) => eventRaised = true;

        monitor.StartMonitoring();
        Thread.Sleep(500);
        monitor.Dispose();

        Assert.IsFalse(eventRaised, "No events should be raised when space is sufficient");
    }

    [TestMethod]
    public void Monitoring_WarningRaisedOnlyOnce()
    {
        var warningCount = 0;
        var monitor = new DiskSpaceMonitor(TEST_PATH, _ => 80 * MB);
        monitor.LowSpaceWarning += (_, _) => Interlocked.Increment(ref warningCount);

        monitor.StartMonitoring();
        Thread.Sleep(500);
        monitor.Dispose();

        Assert.AreEqual(1, warningCount, "Warning should only be raised once per monitoring session");
    }

    [TestMethod]
    public void ClassifyLevel_CriticalSkipsWarning()
    {
        // When space drops directly to critical (below 50 MB),
        // ClassifyLevel returns Critical, not Warning — so the
        // monitor raises CriticalSpaceReached without LowSpaceWarning.
        var level = DiskSpaceMonitor.ClassifyLevel(30 * MB, preSession: false);
        Assert.AreEqual(DiskSpaceLevel.Critical, level);
        Assert.AreNotEqual(DiskSpaceLevel.Warning, level);
    }

    #endregion

    #region DiskSpaceCheckResult Tests

    [TestMethod]
    public void DiskSpaceCheckResult_AvailableMegabytes_ConvertsCorrectly()
    {
        var result = new DiskSpaceCheckResult(512 * MB, DiskSpaceLevel.Ok);

        Assert.AreEqual(512, result.AvailableMegabytes);
        Assert.AreEqual(512 * MB, result.AvailableBytes);
    }

    #endregion

    #region DiskSpaceEventArgs Tests

    [TestMethod]
    public void DiskSpaceEventArgs_AvailableMegabytes_ConvertsCorrectly()
    {
        var args = new DiskSpaceEventArgs(256 * MB, DiskSpaceLevel.Warning);

        Assert.AreEqual(256, args.AvailableMegabytes);
        Assert.AreEqual(256 * MB, args.AvailableBytes);
        Assert.AreEqual(DiskSpaceLevel.Warning, args.Level);
    }

    #endregion

    #region Constructor Validation Tests

    [TestMethod]
    public void Constructor_NullPath_ThrowsArgumentNullException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new DiskSpaceMonitor(null!, _ => 1000 * MB));
    }

    [TestMethod]
    public void Constructor_NullFreeSpaceProvider_ThrowsArgumentNullException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new DiskSpaceMonitor(TEST_PATH, null!));
    }

    #endregion

    #region Threshold Constants Tests

    [TestMethod]
    public void ThresholdConstants_HaveCorrectValues()
    {
        Assert.AreEqual(500 * MB, DiskSpaceMonitor.PRE_SESSION_WARNING_BYTES);
        Assert.AreEqual(100 * MB, DiskSpaceMonitor.WARNING_THRESHOLD_BYTES);
        Assert.AreEqual(50 * MB, DiskSpaceMonitor.CRITICAL_THRESHOLD_BYTES);
    }

    #endregion
}
