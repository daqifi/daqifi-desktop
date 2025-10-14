namespace Daqifi.Desktop.Device;

/// <summary>
/// Provides device type detection logic based on device part numbers.
/// </summary>
public static class DeviceTypeDetector
{
    /// <summary>
    /// Detects the device type from a part number string.
    /// </summary>
    /// <param name="partNumber">The device part number (e.g., "Nq1", "Nq3")</param>
    /// <returns>The detected DeviceType, or DeviceType.Unknown if not recognized</returns>
    public static DeviceType DetectFromPartNumber(string partNumber)
    {
        if (string.IsNullOrWhiteSpace(partNumber))
        {
            return DeviceType.Unknown;
        }

        return partNumber.ToLowerInvariant() switch
        {
            "nq1" => DeviceType.Nyquist1,
            "nq3" => DeviceType.Nyquist3,
            _ => DeviceType.Unknown
        };
    }
}
