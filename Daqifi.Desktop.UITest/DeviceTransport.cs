namespace Daqifi.Desktop.UITest;

/// <summary>
/// Physical transport used to reach the attached DAQiFi device under test.
/// Selectable per test via the DAQIFI_TEST_TRANSPORT environment variable.
/// </summary>
public enum DeviceTransport
{
    /// <summary>WiFi (UDP discovery on port 30303 + TCP streaming).</summary>
    Wifi,

    /// <summary>USB / serial (COM port enumeration).</summary>
    Serial
}
