using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Helpers;
using Brush = System.Windows.Media.Brush;
using DeviceType = Daqifi.Core.Device.DeviceType;

namespace Daqifi.Desktop.ViewModels;

/// <summary>
/// View-model for a single device tile. All presentation (stripe, labels,
/// tile colors) is computed from the underlying device's current state so a
/// device that reconnects or updates its firmware updates in place.
/// </summary>
public sealed class DeviceTileViewModel : ObservableObject, IDisposable
{
    private readonly INotifyPropertyChanged? _deviceNotifier;

    /// <summary>The underlying streaming device this tile represents.</summary>
    public IStreamingDevice Device { get; }

    /// <summary>Primary display name — the device's part number.</summary>
    public string Name => Device.DevicePartNumber;

    /// <summary>Serial number as shown on the tile.</summary>
    public string SerialNumber => Device.DeviceSerialNo;

    /// <summary>User-defined friendly name, or empty when none is set on the device.</summary>
    public string FriendlyName => Device.FriendlyName;

    /// <summary>Firmware version as shown on the tile.</summary>
    public string Version => Device.DeviceVersion;

    /// <summary>
    /// User-friendly detected device type — "Nyquist 1/2/3" for the recognized family,
    /// "Unknown" before detection completes or for unrecognized hardware.
    /// </summary>
    public string DeviceTypeDisplay => Device.DeviceType switch
    {
        DeviceType.Nyquist1 => "Nyquist 1",
        DeviceType.Nyquist2 => "Nyquist 2",
        DeviceType.Nyquist3 => "Nyquist 3",
        _ => "Unknown"
    };

    /// <summary>COM port (USB) or IP address (WiFi).</summary>
    public string Identifier => Device.DisplayIdentifier;

    /// <summary>"USB" or "WIFI" label for the connection chip.</summary>
    public string ConnectionLabel => Device.ConnectionType == ConnectionType.Usb ? "USB" : "WIFI";

    /// <summary>Whether the device is on a USB connection.</summary>
    public bool IsUsb => Device.ConnectionType == ConnectionType.Usb;

    /// <summary>Whether the device needs a firmware update.</summary>
    public bool IsFirmwareOutdated => Device.IsFirmwareOutdated;

    /// <summary>WiFi module firmware version as read from the device (or "Unknown").</summary>
    public string WifiVersion => Device.WifiFirmwareVersion;

    /// <summary>
    /// Whether to show the WiFi firmware version line — only for USB-connected Nyquist (WINC1500)
    /// devices. ESP32-based devices have no separate WiFi firmware version to display.
    /// </summary>
    public bool ShowWifiVersion => IsUsb && Device.HasWincWifiModule;

    /// <summary>Whether the device's WiFi module firmware needs to be flashed.</summary>
    public bool IsWifiFirmwareOutdated => Device.IsWifiFirmwareOutdated;

    /// <summary>Whether the device is currently connected.</summary>
    public bool IsConnected => Device.IsConnected;

    /// <summary>
    /// Type-coded stripe color — cyan for USB, purple for WiFi. Connection
    /// type is a meaningful dimension operators sort by, so it earns a
    /// dedicated hue.
    /// </summary>
    public Brush StripeBrush => IsUsb ? UsbAccent : WifiAccent;

    /// <summary>Background color for the tile.</summary>
    public Brush TileBackground => TileBrushes.SurfaceRaised;

    /// <summary>Border color — stripe color when connected, dim otherwise.</summary>
    public Brush TileBorderBrush => IsConnected ? StripeBrush : TileBrushes.BorderDim;

    /// <summary>Creates a tile bound to the given device.</summary>
    public DeviceTileViewModel(IStreamingDevice device)
    {
        Device = device;
        _deviceNotifier = device as INotifyPropertyChanged;
        if (_deviceNotifier != null)
        {
            _deviceNotifier.PropertyChanged += OnDevicePropertyChanged;
        }
    }

    private void OnDevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IStreamingDevice.IsConnected):
                OnPropertyChanged(nameof(IsConnected));
                OnPropertyChanged(nameof(TileBorderBrush));
                break;
            case nameof(IStreamingDevice.IsFirmwareOutdated):
                OnPropertyChanged(nameof(IsFirmwareOutdated));
                break;
            case nameof(IStreamingDevice.IsWifiFirmwareOutdated):
                OnPropertyChanged(nameof(IsWifiFirmwareOutdated));
                break;
            case nameof(IStreamingDevice.WifiFirmwareVersion):
                OnPropertyChanged(nameof(WifiVersion));
                break;
            case nameof(IStreamingDevice.HasWincWifiModule):
                OnPropertyChanged(nameof(ShowWifiVersion));
                break;
            case nameof(IStreamingDevice.DeviceSerialNo):
                OnPropertyChanged(nameof(SerialNumber));
                break;
            case nameof(IStreamingDevice.FriendlyName):
                OnPropertyChanged(nameof(FriendlyName));
                break;
            case nameof(IStreamingDevice.DeviceVersion):
                OnPropertyChanged(nameof(Version));
                break;
            case nameof(IStreamingDevice.DeviceType):
                OnPropertyChanged(nameof(DeviceTypeDisplay));
                break;
            case nameof(IStreamingDevice.IpAddress):
            case nameof(IStreamingDevice.DisplayIdentifier):
                OnPropertyChanged(nameof(Identifier));
                break;
        }
    }

    /// <summary>Detaches the device subscription.</summary>
    public void Dispose()
    {
        if (_deviceNotifier != null)
        {
            _deviceNotifier.PropertyChanged -= OnDevicePropertyChanged;
        }
    }

    private static readonly Brush UsbAccent = TileBrushes.Frozen("#06B6D4");
    private static readonly Brush WifiAccent = TileBrushes.Frozen("#A855F7");
}
