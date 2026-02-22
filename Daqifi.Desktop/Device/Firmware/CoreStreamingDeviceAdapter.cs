using System.Net;
using Daqifi.Desktop.Device.SerialDevice;
using Daqifi.Core.Communication.Messages;
using CoreScpiMessageProducer = Daqifi.Core.Communication.Producers.ScpiMessageProducer;
using CoreConnectionStatus = Daqifi.Core.Device.ConnectionStatus;
using CoreDeviceStatusEventArgs = Daqifi.Core.Device.DeviceStatusEventArgs;
using CoreMessageReceivedEventArgs = Daqifi.Core.Device.MessageReceivedEventArgs;
using CoreStreamingDevice = Daqifi.Core.Device.IStreamingDevice;

namespace Daqifi.Desktop.Device.Firmware;

/// <summary>
/// Adapts a desktop streaming device to Core's firmware update device contract.
/// </summary>
public sealed class CoreStreamingDeviceAdapter : CoreStreamingDevice
{
    private static readonly string LanFirmwareUpdateCommand = CoreScpiMessageProducer.SetLanFirmwareUpdateMode.Data;

    private readonly IStreamingDevice _desktopDevice;

    public CoreStreamingDeviceAdapter(IStreamingDevice desktopDevice)
    {
        _desktopDevice = desktopDevice ?? throw new ArgumentNullException(nameof(desktopDevice));
    }

    public string Name => _desktopDevice is SerialStreamingDevice serialDevice
        ? serialDevice.PortName
        : _desktopDevice.Name;

    public IPAddress? IpAddress => IPAddress.TryParse(_desktopDevice.IpAddress, out var ip)
        ? ip
        : null;

    public bool IsConnected => _desktopDevice.IsConnected;

    public CoreConnectionStatus Status => IsConnected
        ? CoreConnectionStatus.Connected
        : CoreConnectionStatus.Disconnected;

    public event EventHandler<CoreDeviceStatusEventArgs>? StatusChanged;
    public event EventHandler<CoreMessageReceivedEventArgs>? MessageReceived;

    public int StreamingFrequency
    {
        get => _desktopDevice.StreamingFrequency;
        set => _desktopDevice.StreamingFrequency = value;
    }

    public bool IsStreaming => _desktopDevice is AbstractStreamingDevice streamingDevice &&
                               streamingDevice.IsStreaming;

    public void StartStreaming() => _desktopDevice.InitializeStreaming();

    public void StopStreaming() => _desktopDevice.StopStreaming();

    public void Connect()
    {
        var previous = Status;
        _ = _desktopDevice.Connect();
        RaiseStatusChanged(previous);
    }

    public void Disconnect()
    {
        var previous = Status;
        _ = _desktopDevice.Disconnect();
        RaiseStatusChanged(previous);
    }

    public void Send<T>(IOutboundMessage<T> message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message is IOutboundMessage<string> commandMessage)
        {
            if (_desktopDevice is not SerialStreamingDevice serialDevice)
            {
                throw new NotSupportedException(
                    $"Firmware transport requires a serial device, but got '{_desktopDevice.GetType().Name}'.");
            }

            if (IsLanFirmwareUpdateModeCommand(commandMessage))
            {
                // Preserve legacy sequence used by desktop before core migration.
                serialDevice.EnableLanUpdateMode();
                return;
            }

            serialDevice.SendScpiMessage(commandMessage);
            return;
        }

        throw new NotSupportedException(
            $"Unsupported outbound message payload type '{typeof(T).Name}' for desktop transport.");
    }

    private void RaiseStatusChanged(CoreConnectionStatus previous)
    {
        var current = Status;
        if (current == previous)
        {
            return;
        }

        StatusChanged?.Invoke(this, new CoreDeviceStatusEventArgs(current));
    }

    private static bool IsLanFirmwareUpdateModeCommand(IOutboundMessage<string> message)
    {
        return string.Equals(
            message.Data?.Trim(),
            LanFirmwareUpdateCommand,
            StringComparison.OrdinalIgnoreCase);
    }
}

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
