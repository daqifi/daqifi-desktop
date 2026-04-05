using Daqifi.Desktop.Common.Loggers;
using System.IO;

namespace Daqifi.Desktop.DiskSpace;

/// <summary>
/// Monitors available disk space on the drive containing the DAQiFi data directory
/// and raises events when predefined thresholds are crossed.
/// </summary>
public class DiskSpaceMonitor : IDiskSpaceMonitor
{
    #region Constants
    /// <summary>Pre-session warning: 500 MB.</summary>
    public const long PRE_SESSION_WARNING_BYTES = 500L * 1024 * 1024;

    /// <summary>Active session warning: 100 MB.</summary>
    public const long WARNING_THRESHOLD_BYTES = 100L * 1024 * 1024;

    /// <summary>Hard stop: 50 MB.</summary>
    public const long CRITICAL_THRESHOLD_BYTES = 50L * 1024 * 1024;

    private const int MONITOR_INTERVAL_MS = 15_000;
    #endregion

    #region Private Fields
    private readonly AppLogger _appLogger = AppLogger.Instance;
    private readonly string _monitoredPath;
    private readonly Func<string, long> _getAvailableFreeSpace;
    private System.Threading.Timer? _timer;
    private bool _disposed;
    private bool _warningRaised;
    #endregion

    #region Events
    public event EventHandler<DiskSpaceEventArgs>? LowSpaceWarning;
    public event EventHandler<DiskSpaceEventArgs>? CriticalSpaceReached;
    #endregion

    #region Properties
    public bool IsMonitoring => _timer != null;
    #endregion

    #region Constructor
    /// <summary>
    /// Creates a new disk space monitor for the specified path.
    /// </summary>
    /// <param name="monitoredPath">Path on the drive to monitor (typically the data directory).</param>
    public DiskSpaceMonitor(string monitoredPath)
        : this(monitoredPath, GetAvailableFreeSpaceForPath)
    {
    }

    /// <summary>
    /// Creates a new disk space monitor with an injectable free-space provider for testing.
    /// </summary>
    internal DiskSpaceMonitor(string monitoredPath, Func<string, long> getAvailableFreeSpace)
    {
        _monitoredPath = monitoredPath ?? throw new ArgumentNullException(nameof(monitoredPath));
        _getAvailableFreeSpace = getAvailableFreeSpace ?? throw new ArgumentNullException(nameof(getAvailableFreeSpace));
    }
    #endregion

    #region Public Methods
    /// <inheritdoc />
    public DiskSpaceCheckResult CheckPreLoggingSpace()
    {
        try
        {
            var available = GetAvailableSpace();
            var level = ClassifyLevel(available, preSession: true);

            _appLogger.Information($"Pre-logging disk space check: {available / (1024 * 1024)} MB available, level={level}");

            return new DiskSpaceCheckResult(available, level);
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Failed to check disk space — assuming OK to avoid blocking logging");
            return new DiskSpaceCheckResult(long.MaxValue, DiskSpaceLevel.Ok);
        }
    }

    /// <inheritdoc />
    public void StartMonitoring()
    {
        if (_timer != null)
        {
            return;
        }

        _warningRaised = false;
        _timer = new System.Threading.Timer(OnTimerTick, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(MONITOR_INTERVAL_MS));
        _appLogger.Information("Disk space monitoring started");
    }

    /// <inheritdoc />
    public void StopMonitoring()
    {
        if (_timer == null)
        {
            return;
        }

        _timer.Dispose();
        _timer = null;
        _warningRaised = false;
        _appLogger.Information("Disk space monitoring stopped");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopMonitoring();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
    #endregion

    #region Private Methods
    private void OnTimerTick(object? state)
    {
        try
        {
            var available = GetAvailableSpace();
            var level = ClassifyLevel(available, preSession: false);

            switch (level)
            {
                case DiskSpaceLevel.Critical:
                    // Stop the timer first to prevent duplicate critical events
                    // before the UI thread can call StopMonitoring()
                    _timer?.Change(Timeout.Infinite, Timeout.Infinite);
                    _appLogger.Warning($"Disk space critically low: {available / (1024 * 1024)} MB — triggering hard stop");
                    CriticalSpaceReached?.Invoke(this, new DiskSpaceEventArgs(available, DiskSpaceLevel.Critical));
                    break;

                case DiskSpaceLevel.Warning when !_warningRaised:
                    _appLogger.Warning($"Disk space low: {available / (1024 * 1024)} MB");
                    _warningRaised = true;
                    LowSpaceWarning?.Invoke(this, new DiskSpaceEventArgs(available, DiskSpaceLevel.Warning));
                    break;
            }
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Error checking disk space during monitoring");
        }
    }

    private long GetAvailableSpace()
    {
        return _getAvailableFreeSpace(_monitoredPath);
    }

    internal static DiskSpaceLevel ClassifyLevel(long availableBytes, bool preSession)
    {
        if (availableBytes < CRITICAL_THRESHOLD_BYTES)
        {
            return DiskSpaceLevel.Critical;
        }

        if (availableBytes < WARNING_THRESHOLD_BYTES)
        {
            return DiskSpaceLevel.Warning;
        }

        if (preSession && availableBytes < PRE_SESSION_WARNING_BYTES)
        {
            return DiskSpaceLevel.PreSessionWarning;
        }

        return DiskSpaceLevel.Ok;
    }

    private static long GetAvailableFreeSpaceForPath(string path)
    {
        var driveInfo = new DriveInfo(Path.GetPathRoot(path)!);
        return driveInfo.AvailableFreeSpace;
    }
    #endregion
}
