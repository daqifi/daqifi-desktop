namespace Daqifi.Desktop.DiskSpace;

/// <summary>
/// Provides data for disk space threshold events.
/// </summary>
public class DiskSpaceEventArgs : EventArgs
{
    /// <summary>
    /// Available disk space in bytes at the time the event was raised.
    /// </summary>
    public long AvailableBytes { get; }

    /// <summary>
    /// Available disk space in megabytes at the time the event was raised.
    /// </summary>
    public long AvailableMegabytes => AvailableBytes / (1024 * 1024);

    /// <summary>
    /// The threshold level that was crossed.
    /// </summary>
    public DiskSpaceLevel Level { get; }

    public DiskSpaceEventArgs(long availableBytes, DiskSpaceLevel level)
    {
        AvailableBytes = availableBytes;
        Level = level;
    }
}
