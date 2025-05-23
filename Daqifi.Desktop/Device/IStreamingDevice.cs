﻿using Daqifi.Desktop.Channel;
using Daqifi.Desktop.DataModel.Channel;
using Daqifi.Desktop.DataModel.Network;
using Daqifi.Desktop.IO.Messages.Consumers;
using Daqifi.Desktop.IO.Messages.Producers;
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
    IMessageConsumer MessageConsumer { get; set; }
    IMessageProducer MessageProducer { get; set; }
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

    void UpdateNetworkConfiguration();
}