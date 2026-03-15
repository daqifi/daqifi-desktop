using System.Net;
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
}
