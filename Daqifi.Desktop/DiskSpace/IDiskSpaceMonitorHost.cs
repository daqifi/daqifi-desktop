namespace Daqifi.Desktop.DiskSpace;

/// <summary>
/// Narrow seam the <see cref="DiskSpaceMonitorCoordinator"/> uses to push disk-space outcomes back
/// to the host view model.
/// <para>
/// Implemented by <c>DaqifiViewModel</c>. The coordinator owns the <see cref="IDiskSpaceMonitor"/>,
/// the pre-logging gate, and the low/critical event handling, while the two view concerns it cannot
/// own — stopping the active logging session and presenting a dialog — are delegated here. Both
/// members are called from the monitor's background timer thread as well as the UI thread, so the
/// host implementation is responsible for marshalling to the dispatcher (issue #592).
/// </para>
/// </summary>
public interface IDiskSpaceMonitorHost
{
    /// <summary>
    /// Stops the active logging session. Invoked when disk space reaches the critical threshold
    /// during monitoring. Called from the monitor's timer thread, so the implementation marshals to
    /// the UI thread.
    /// </summary>
    void StopLogging();

    /// <summary>
    /// Presents an informational disk-space dialog. Dialog presentation (and its UI-thread
    /// marshalling) is a view concern, so the coordinator delegates it here rather than touching WPF
    /// directly. Safe to call from either the UI thread (the pre-logging gate) or the monitor's timer
    /// thread (the low/critical events).
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">The dialog body.</param>
    Task ShowDiskSpaceMessageAsync(string title, string message);
}
