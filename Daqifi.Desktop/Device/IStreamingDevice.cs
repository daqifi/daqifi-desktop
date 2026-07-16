using Daqifi.Desktop.Channel;
using ChannelDirection = Daqifi.Core.Channel.ChannelDirection;
using Daqifi.Core.Device.Network;
using Daqifi.Core.Device.SdCard;
using Daqifi.Desktop.Models;

namespace Daqifi.Desktop.Device;

public enum DeviceMode
{
    StreamToApp,
    LogToDevice
}

public enum ConnectionType
{
    /// <summary>
    /// Device is connected via USB
    /// </summary>
    Usb,

    /// <summary>
    /// Device is connected via WiFi
    /// </summary>
    Wifi
}

public interface IStreamingDevice : IDevice
{
    DeviceMode Mode { get; }
    ConnectionType ConnectionType { get; }
    bool IsConnected { get; }
    bool IsLoggingToSdCard { get; }
    IReadOnlyList<SdCardFile> SdCardFiles { get; }

    /// <summary>
    /// Gets or sets the SD card logging format used when starting SD card logging.
    /// </summary>
    SdCardLogFormat SdCardLogFormat { get; set; }

    void SwitchMode(DeviceMode newMode);
    void StartSdCardLogging();
    void StopSdCardLogging();
    void RefreshSdCardFiles();
    void UpdateSdCardFiles(List<SdCardFile> files);
    string DevicePartNumber { get; }

    /// <summary>
    /// Gets the device's hardware timestamp clock frequency in Hz.
    /// Used as a fallback when parsing SD card files that lack this field.
    /// </summary>
    uint TimestampFrequency { get; }
    NetworkConfiguration NetworkConfiguration { get; }
    string MacAddress { get; set; }
    string DeviceSerialNo { get; set; }
    string DeviceVersion { get; set; }
    bool IsFirmwareOutdated { get; set; }

    /// <summary>
    /// Gets whether this device has a separately-flashable WINC1500 WiFi module — i.e. it is
    /// part of the Nyquist family. ESP32-based and unrecognized devices integrate WiFi into the
    /// SoC and have no WINC firmware to query or flash, so the WiFi-firmware check is skipped
    /// for them.
    /// </summary>
    bool HasWincWifiModule { get; }

    /// <summary>
    /// Gets or sets whether the device's WiFi module firmware needs to be flashed —
    /// either because the reported version is below the minimum supported version or
    /// because the WiFi chip-info query failed. Only meaningful for USB-connected
    /// devices with a WINC1500 module (see <see cref="HasWincWifiModule"/>).
    /// </summary>
    bool IsWifiFirmwareOutdated { get; set; }

    /// <summary>
    /// Gets or sets the WiFi module firmware version reported by the device, or
    /// <c>"Unknown"</c> when the chip-info query could not be completed.
    /// </summary>
    string WifiFirmwareVersion { get; set; }
    string IpAddress { get; set; }
    int StreamingFrequency { get; set; }
    
    /// <summary>
    /// Gets the appropriate display identifier for this device based on connection type.
    /// Returns COM port for USB devices, IP address for WiFi devices.
    /// </summary>
    string DisplayIdentifier { get; }

    /// <summary>
    /// Gets the best available human-readable name for this device.
    /// Returns the user-defined friendly name when set, then the serial number,
    /// otherwise falls back to DisplayIdentifier.
    /// </summary>
    string DeviceDisplayName { get; }

    /// <summary>
    /// Gets the device's user-defined friendly name (<c>friendly_device_name</c> in the
    /// protobuf message), or an empty string when none is set. Populated from the device's
    /// <c>SYSTem:SYSInfoPB?</c> info response, which Core requests once during connect — not
    /// available until shortly after the device connects.
    /// </summary>
    string FriendlyName { get; }

    /// <summary>
    /// Sets and persists a user-defined friendly name to the device's NVM via the
    /// <c>SYSTem:DEVice:NAME</c> / <c>SYSTem:DEVice:NAME:SAVE</c> SCPI commands. A missing
    /// or disconnected device logs and no-ops rather than throwing (same rationale as
    /// <see cref="SetChannelOutputValue"/>).
    /// </summary>
    /// <param name="name">
    /// 1-31 printable ASCII characters (0x20-0x7E); cannot contain <c>"</c> or <c>\</c>.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> fails validation.</exception>
    void SetFriendlyName(string name);

    List<IChannel> DataChannels { get; set; }

    void InitializeStreaming();
    void StopStreaming();

    /// <summary>
    /// Sends a command to get any initialization data from the streamingDevice that might be needed
    /// </summary>
    void InitializeDeviceState();

    /// <summary>
    /// Sends a command to activate a channel on the streamingDevice
    /// </summary>
    void AddChannel(IChannel channel);

    /// <summary>
    /// Sends a command to deactivate a channel on the streamingDevice
    /// </summary>
    void RemoveChannel(IChannel channel);

    /// <summary>
    /// Enables multiple channels on the device with a single SCPI command per channel type.
    /// </summary>
    void AddChannels(IEnumerable<IChannel> channels);

    /// <summary>
    /// Disables all channels on the device.
    /// </summary>
    void RemoveAllChannels();

    /// <summary>
    /// Drives a digital output channel's state through the Core device (values >= 0.5 drive
    /// high). Non-digital channels are ignored. When the device is missing or disconnected the
    /// call logs and no-ops instead of throwing — it can originate from UI property setters
    /// racing a disconnect.
    /// </summary>
    void SetChannelOutputValue(IChannel channel, double value);

    /// <summary>
    /// Sets a digital channel's I/O direction through the Core device. Non-digital channels
    /// are ignored, and a missing or disconnected device logs and no-ops rather than throwing
    /// (same rationale as <see cref="SetChannelOutputValue"/>).
    /// </summary>
    void SetChannelDirection(IChannel channel, ChannelDirection direction);

    /// <summary>
    /// Starts or stops PWM output on a digital channel through the Core device. Enabling
    /// resends the channel's duty cycle and the device-wide frequency first (Core's
    /// documented duty → frequency → enable order), so the device always runs exactly what
    /// the UI shows. Non-digital channels are ignored, and a missing or disconnected device
    /// logs and no-ops rather than throwing (same rationale as
    /// <see cref="SetChannelOutputValue"/>).
    /// </summary>
    void SetChannelPwmEnabled(IChannel channel, bool enabled);

    /// <summary>
    /// Sets a digital channel's PWM duty cycle (1-100 percent) through the Core device;
    /// takes effect immediately while the channel's PWM output is enabled. Non-digital
    /// channels are ignored, and a missing or disconnected device logs and no-ops rather
    /// than throwing (same rationale as <see cref="SetChannelOutputValue"/>).
    /// </summary>
    void SetChannelPwmDutyCycle(IChannel channel, int dutyCyclePercent);

    /// <summary>
    /// Gets or sets the device-wide PWM frequency in hertz (6-50000, coerced). All PWM
    /// channels share one hardware timer, so there is exactly one frequency per device.
    /// Setting it commands the device immediately (a live change rescales every enabled
    /// PWM channel), and the value is also resent whenever a channel's PWM output is
    /// enabled.
    /// </summary>
    int PwmFrequencyHz { get; set; }

    Task UpdateNetworkConfiguration();

    /// <summary>
    /// Downloads a file from the device's SD card over USB to a temporary file.
    /// </summary>
    Task<SdCardDownloadResult> DownloadSdCardFileAsync(
        string fileName,
        IProgress<SdCardTransferProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a file from the device's SD card.
    /// </summary>
    Task DeleteSdCardFileAsync(string fileName, CancellationToken ct = default);

    /// <summary>
    /// Builds an <see cref="SdCardDeviceConfiguration"/> from this device's channel
    /// configuration — calibration, resolution, port range, and internal scale — so the
    /// SD card parser can convert raw ADC counts to real voltage values. Returns <c>null</c>
    /// when the device has no analog channels or is not connected via USB.
    /// </summary>
    SdCardDeviceConfiguration? GetSdCardParseConfiguration();
}
