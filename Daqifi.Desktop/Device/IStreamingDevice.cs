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

    void SwitchMode(DeviceMode newMode);
    void StartSdCardLogging();
    void StopSdCardLogging();
    void RefreshSdCardFiles();
    void UpdateSdCardFiles(List<SdCardFile> files);
    string DevicePartNumber { get; }
    NetworkConfiguration NetworkConfiguration { get; }
    string MacAddress { get; set; }
    string DeviceSerialNo { get; set; }
    string DeviceVersion { get; set; }
    bool IsFirmwareOutdated { get; set; }
    string IpAddress { get; set; }
    int StreamingFrequency { get; set; }
    
    /// <summary>
    /// Gets the appropriate display identifier for this device based on connection type.
    /// Returns COM port for USB devices, IP address for WiFi devices.
    /// </summary>
    string DisplayIdentifier { get; }

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

    void SetChannelOutputValue(IChannel channel, double value);

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
