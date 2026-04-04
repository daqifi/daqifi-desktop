namespace Daqifi.Desktop.DiskSpace;

/// <summary>
/// Result of a pre-logging disk space check.
/// </summary>
public class DiskSpaceCheckResult
{
    /// <summary>
    /// Available disk space in bytes.
    /// </summary>
    public long AvailableBytes { get; }

    /// <summary>
    /// Available disk space in megabytes.
    /// </summary>
    public long AvailableMegabytes => AvailableBytes / (1024 * 1024);

    /// <summary>
    /// The disk space level determined by the check.
    /// </summary>
    public DiskSpaceLevel Level { get; }

    public DiskSpaceCheckResult(long availableBytes, DiskSpaceLevel level)
    {
        AvailableBytes = availableBytes;
        Level = level;
    }
}
