using Daqifi.Desktop.Common.Loggers;

namespace Daqifi.Desktop.DiskSpace;

/// <summary>
/// Owns disk-space gating and monitoring for a logging session: the pre-logging space check that can
/// block or warn before logging starts, the periodic <see cref="IDiskSpaceMonitor"/> that runs while
/// logging is active, and the low/critical threshold handling (warn, or auto-stop logging).
/// <para>
/// Extracted from <c>DaqifiViewModel</c> (issue #592). The monitor and logger are constructor-injected
/// and the two view concerns it cannot own — stopping the session and presenting a dialog — are
/// reached through the <see cref="IDiskSpaceMonitorHost"/> seam, so the coordinator has no dependency
/// on WPF or on desktop singletons (<c>AppLogger.Instance</c>, <c>App.DaqifiDataDirectory</c>) and is
/// unit-testable in isolation.
/// </para>
/// </summary>
public sealed class DiskSpaceMonitorCoordinator : IDisposable
{
    #region Private Fields
    private readonly IDiskSpaceMonitorHost _host;
    private readonly IDiskSpaceMonitor _monitor;
    private readonly IAppLogger _appLogger;
    private bool _disposed;
    #endregion

    #region Constructor
    /// <summary>
    /// Creates the coordinator and subscribes to the monitor's threshold events. The composition root
    /// (the view model) builds the production <see cref="DiskSpaceMonitor"/> and passes it here so this
    /// class never news up a monitor or reaches into singletons.
    /// </summary>
    /// <param name="host">The host view-model surface used to stop logging and present dialogs.</param>
    /// <param name="monitor">The disk-space monitor this coordinator owns and disposes.</param>
    /// <param name="appLogger">Application logger used for diagnostics.</param>
    public DiskSpaceMonitorCoordinator(IDiskSpaceMonitorHost host, IDiskSpaceMonitor monitor, IAppLogger appLogger)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _appLogger = appLogger ?? throw new ArgumentNullException(nameof(appLogger));

        _monitor.LowSpaceWarning += OnLowSpaceWarning;
        _monitor.CriticalSpaceReached += OnCriticalSpaceReached;
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// Evaluates whether logging may start given the currently available disk space. When space is
    /// critically low this returns <see cref="DiskSpaceStartDecision.Blocked"/> after presenting a
    /// blocking dialog; when space is low (but not critical) it returns
    /// <see cref="DiskSpaceStartDecision.AllowedWithWarning"/> after presenting a warning dialog;
    /// otherwise it returns <see cref="DiskSpaceStartDecision.Allowed"/>.
    /// </summary>
    public DiskSpaceStartDecision EvaluateStartLogging()
    {
        var check = _monitor.CheckPreLoggingSpace();

        if (check.Level == DiskSpaceLevel.Critical)
        {
            _ = _host.ShowDiskSpaceMessageAsync(
                "Cannot Start Logging",
                $"Only {check.AvailableMegabytes} MB of disk space remaining. " +
                "Logging cannot start because the disk is critically low.\n\n" +
                "Please free disk space by deleting old logging sessions or removing other files.");
            return DiskSpaceStartDecision.Blocked;
        }

        if (check.Level == DiskSpaceLevel.PreSessionWarning || check.Level == DiskSpaceLevel.Warning)
        {
            _ = _host.ShowDiskSpaceMessageAsync(
                "Low Disk Space Warning",
                $"Only {check.AvailableMegabytes} MB of disk space remaining. " +
                "Logging may be stopped automatically if space runs out.\n\n" +
                "Consider freeing disk space by deleting old logging sessions or removing other files.");
            return DiskSpaceStartDecision.AllowedWithWarning;
        }

        return DiskSpaceStartDecision.Allowed;
    }

    /// <summary>Starts periodic disk-space monitoring for the active logging session.</summary>
    /// <param name="suppressInitialWarning">
    /// When true, suppresses the first warning-level notification (because a pre-session warning was
    /// already shown by <see cref="EvaluateStartLogging"/>).
    /// </param>
    public void StartMonitoring(bool suppressInitialWarning) => _monitor.StartMonitoring(suppressInitialWarning);

    /// <summary>Stops periodic disk-space monitoring.</summary>
    public void StopMonitoring() => _monitor.StopMonitoring();
    #endregion

    #region Event Handlers
    private void OnLowSpaceWarning(object? sender, DiskSpaceEventArgs e)
    {
        _ = _host.ShowDiskSpaceMessageAsync(
            "Low Disk Space Warning",
            $"Only {e.AvailableMegabytes} MB of disk space remaining. " +
            "Logging will be stopped automatically if space drops below 50 MB.\n\n" +
            "Consider freeing disk space by deleting old logging sessions or removing other files.");
    }

    private void OnCriticalSpaceReached(object? sender, DiskSpaceEventArgs e)
    {
        _appLogger.Warning($"Disk space critical ({e.AvailableMegabytes} MB) — automatically stopping logging");
        _host.StopLogging();

        _ = _host.ShowDiskSpaceMessageAsync(
            "Logging Stopped — Disk Space Critical",
            $"Logging was automatically stopped because disk space dropped to {e.AvailableMegabytes} MB.\n\n" +
            "To prevent system instability, logging has been halted. " +
            "Please free disk space by deleting old logging sessions or removing other files before resuming.");
    }
    #endregion

    #region IDisposable
    /// <summary>Unsubscribes from the monitor's events and disposes it. Call on application shutdown.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _monitor.LowSpaceWarning -= OnLowSpaceWarning;
        _monitor.CriticalSpaceReached -= OnCriticalSpaceReached;
        _monitor.Dispose();
        _disposed = true;
    }
    #endregion
}
