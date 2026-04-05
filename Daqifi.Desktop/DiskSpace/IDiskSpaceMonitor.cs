namespace Daqifi.Desktop.DiskSpace;

/// <summary>
/// Monitors available disk space and raises events when thresholds are crossed.
/// </summary>
public interface IDiskSpaceMonitor : IDisposable
{
    /// <summary>
    /// Raised when available disk space drops below the warning threshold (100 MB).
    /// </summary>
    event EventHandler<DiskSpaceEventArgs> LowSpaceWarning;

    /// <summary>
    /// Raised when available disk space drops below the critical threshold (50 MB),
    /// indicating logging must be stopped immediately.
    /// </summary>
    event EventHandler<DiskSpaceEventArgs> CriticalSpaceReached;

    /// <summary>
    /// Checks whether there is sufficient disk space to begin a logging session.
    /// </summary>
    /// <returns>A result indicating the space level and available bytes.</returns>
    DiskSpaceCheckResult CheckPreLoggingSpace();

    /// <summary>
    /// Starts periodic monitoring of disk space during an active logging session.
    /// </summary>
    /// <param name="suppressInitialWarning">
    /// When true, suppresses the first warning-level notification (e.g., because a pre-session
    /// warning was already shown to the user).
    /// </param>
    void StartMonitoring(bool suppressInitialWarning = false);

    /// <summary>
    /// Stops periodic disk space monitoring.
    /// </summary>
    void StopMonitoring();

    /// <summary>
    /// Whether monitoring is currently active.
    /// </summary>
    bool IsMonitoring { get; }
}
