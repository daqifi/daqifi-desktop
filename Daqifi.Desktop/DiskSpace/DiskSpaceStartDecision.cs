namespace Daqifi.Desktop.DiskSpace;

/// <summary>
/// Outcome of the pre-logging disk-space gate evaluated by
/// <see cref="DiskSpaceMonitorCoordinator.EvaluateStartLogging"/>.
/// </summary>
public sealed class DiskSpaceStartDecision
{
    /// <summary>
    /// Whether logging may start. <c>false</c> only when the disk is critically low, in which case
    /// the coordinator has already surfaced the blocking dialog.
    /// </summary>
    public bool CanStart { get; }

    /// <summary>
    /// When <c>true</c>, a low-space warning was already shown to the user, so monitoring should
    /// suppress its first in-session warning notification (passed as
    /// <c>suppressInitialWarning</c> to <see cref="DiskSpaceMonitorCoordinator.StartMonitoring"/>).
    /// </summary>
    public bool SuppressInitialWarning { get; }

    private DiskSpaceStartDecision(bool canStart, bool suppressInitialWarning)
    {
        CanStart = canStart;
        SuppressInitialWarning = suppressInitialWarning;
    }

    /// <summary>Disk critically low — logging must not start; the blocking dialog was shown.</summary>
    public static readonly DiskSpaceStartDecision Blocked = new(canStart: false, suppressInitialWarning: false);

    /// <summary>Sufficient space — logging may start with no warning shown.</summary>
    public static readonly DiskSpaceStartDecision Allowed = new(canStart: true, suppressInitialWarning: false);

    /// <summary>Space is low but not critical — logging may start; a warning dialog was shown.</summary>
    public static readonly DiskSpaceStartDecision AllowedWithWarning = new(canStart: true, suppressInitialWarning: true);
}
