using System.Net;
using Daqifi.Core.Channel;
using Daqifi.Core.Communication.Messages;
using CoreConnectionStatus = Daqifi.Core.Device.ConnectionStatus;
using CoreDeviceStatusEventArgs = Daqifi.Core.Device.DeviceStatusEventArgs;
using CoreMessageReceivedEventArgs = Daqifi.Core.Device.MessageReceivedEventArgs;
using CoreStreamingDevice = Daqifi.Core.Device.IStreamingDevice;

namespace Daqifi.Desktop.Device.Firmware;

/// <summary>
/// Minimal adapter for manual bootloader-only update dialogs.
/// The device is already in firmware mode, so serial operations are intentionally no-op.
/// </summary>
public sealed class BootloaderSessionStreamingDeviceAdapter : CoreStreamingDevice
{
    private bool _isConnected = true;

    public BootloaderSessionStreamingDeviceAdapter(string name)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "DAQiFi Bootloader" : name;
    }

    public string Name { get; }
    public IPAddress? IpAddress => null;
    public bool IsConnected => _isConnected;
    public CoreConnectionStatus Status => _isConnected
        ? CoreConnectionStatus.Connected
        : CoreConnectionStatus.Disconnected;

    public event EventHandler<CoreDeviceStatusEventArgs>? StatusChanged;
    public event EventHandler<CoreMessageReceivedEventArgs>? MessageReceived;

    public int StreamingFrequency { get; set; } = 1;
    public bool IsStreaming => false;

    public void StartStreaming()
    {
    }

    public void StopStreaming()
    {
    }

    public void Connect()
    {
        if (_isConnected)
        {
            return;
        }

        _isConnected = true;
        StatusChanged?.Invoke(this, new CoreDeviceStatusEventArgs(CoreConnectionStatus.Connected));
    }

    public void Disconnect()
    {
        if (!_isConnected)
        {
            return;
        }

        _isConnected = false;
        StatusChanged?.Invoke(this, new CoreDeviceStatusEventArgs(CoreConnectionStatus.Disconnected));
    }

    public void Send<T>(IOutboundMessage<T> message)
    {
        // No-op by design: bootloader-only flow does not use desktop serial command transport.
    }

    // Channel management and device-control members (added to Core's IStreamingDevice in 0.24.0)
    // are intentionally no-op here: the device is already in firmware-update mode and this adapter
    // does not configure channels, drive outputs, or reboot through the streaming command path.
    public void EnableChannel(IChannel channel)
    {
    }

    public void EnableChannels(IEnumerable<IChannel> channels)
    {
    }

    public void DisableChannel(IChannel channel)
    {
    }

    public void DisableAllChannels()
    {
    }

    public void SetDioDirection(IChannel channel, ChannelDirection direction)
    {
    }

    public void SetDioValue(IChannel channel, bool value)
    {
    }

    public void SetPwmEnabled(IChannel channel, bool enabled)
    {
    }

    public void SetPwmDutyCycle(IChannel channel, int dutyCyclePercent)
    {
    }

    public void SetPwmFrequency(int frequencyHz)
    {
    }

    public int PwmFrequencyHz => 0;

    public void SetAnalogOutput(int channelNumber, double voltage)
    {
    }

    public void Reboot()
    {
    }
}
