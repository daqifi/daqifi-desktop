namespace Daqifi.Desktop.DiskSpace;

/// <summary>
/// Represents the severity level of a disk space check.
/// </summary>
public enum DiskSpaceLevel
{
    /// <summary>
    /// Sufficient disk space available.
    /// </summary>
    Ok,

    /// <summary>
    /// Below 500 MB — pre-session warning threshold.
    /// </summary>
    PreSessionWarning,

    /// <summary>
    /// Below 100 MB — active session warning threshold.
    /// </summary>
    Warning,

    /// <summary>
    /// Below 50 MB — logging must be stopped immediately.
    /// </summary>
    Critical
}
