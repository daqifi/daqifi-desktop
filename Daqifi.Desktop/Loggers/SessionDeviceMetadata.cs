namespace Daqifi.Desktop.Logger;

/// <summary>
/// Per-device metadata captured at the start of a logging session.
/// Stores the configured sampling frequency and device identification so
/// that the session UI can display this information without re-deriving
/// it from sample timestamps.
/// </summary>
/// <remarks>
/// One row is created per (session, device) at logging start. Rows are
/// removed via cascade delete when the parent <see cref="LoggingSession"/>
/// is deleted.
/// </remarks>
public class SessionDeviceMetadata
{
    #region Properties
    /// <summary>
    /// Foreign key to the parent <see cref="LoggingSession"/>. Part of the composite primary key.
    /// </summary>
    public int LoggingSessionID { get; set; }

    /// <summary>
    /// Device serial number. Part of the composite primary key so that
    /// multi-device sessions get one row per device.
    /// </summary>
    public string DeviceSerialNo { get; set; }

    /// <summary>
    /// Friendly device name captured at log start (e.g., "Nyquist 1").
    /// </summary>
    public string DeviceName { get; set; }

    /// <summary>
    /// Configured sampling frequency in Hz at the time logging started.
    /// </summary>
    public int SamplingFrequencyHz { get; set; }

    /// <summary>
    /// Navigation property to the parent session.
    /// </summary>
    public LoggingSession LoggingSession { get; set; }
    #endregion
}
