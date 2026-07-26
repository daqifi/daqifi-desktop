namespace Daqifi.Desktop.Device;

/// <summary>
/// One decoded streaming frame's device-level metadata, dispatched to the logging pipeline
/// alongside the frame's channel samples. The string members default to <see cref="string.Empty"/>
/// because a frame is populated field by field from an incoming protobuf message, and a field the
/// device omitted must leave an empty value behind rather than a null every logging and export path
/// would have to guard.
/// </summary>
public class DeviceMessage
{
    /// <summary>Identifier of the logging session this frame is recorded under.</summary>
    public int LoggingSessionID { get; set; }

    /// <summary>
    /// Rollover-corrected device timestamp for the frame, as <see cref="DateTime"/> ticks.
    /// </summary>
    public long TimestampTicks { get; init; }

    /// <summary>
    /// Host wall-clock time when the frame was processed, as <see cref="DateTime"/> ticks. Paired
    /// with <see cref="TimestampTicks"/> so device time can be related back to application time.
    /// </summary>
    public long AppTicks { get; init; }

    /// <summary>Friendly name of the device that produced the frame.</summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Serial number of the device that produced the frame.</summary>
    public string DeviceSerialNo { get; set; } = string.Empty;

    /// <summary>Firmware revision reported by the device.</summary>
    public string DeviceVersion { get; set; } = string.Empty;

    /// <summary>Number of digital channels reported for the frame.</summary>
    public int DigitalChannelCount { get; set; }

    /// <summary>Number of analog channels reported for the frame.</summary>
    public int AnalogChannelCount { get; set; }

    /// <summary>Device status word from the protobuf message.</summary>
    public int DeviceStatus { get; set; }

    /// <summary>Power status word from the protobuf message.</summary>
    public int PowerStatus { get; set; }

    /// <summary>Battery status word from the protobuf message.</summary>
    public int BatteryStatus { get; set; }

    /// <summary>Temperature status word from the protobuf message.</summary>
    public int TempStatus { get; set; }

    /// <summary>Device timestamp-counter frequency, in hertz.</summary>
    public int TargetFrequency { get; set; }

    /// <summary>True when the device's timestamp counter wrapped on this frame.</summary>
    public bool Rollover { get; set; }
}
