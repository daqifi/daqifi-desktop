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
    /// Returns the serial number when populated, otherwise falls back to DisplayIdentifier.
    /// </summary>
    string DeviceDisplayName { get; }

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
}
