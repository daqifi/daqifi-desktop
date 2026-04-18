using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Daqifi.Desktop.Device;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

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

    /// <summary>Firmware version as shown on the tile.</summary>
    public string Version => Device.DeviceVersion;

    /// <summary>COM port (USB) or IP address (WiFi).</summary>
    public string Identifier => Device.DisplayIdentifier;

    /// <summary>"USB" or "WIFI" label for the connection chip.</summary>
    public string ConnectionLabel => Device.ConnectionType == ConnectionType.Usb ? "USB" : "WIFI";

    /// <summary>Whether the device is on a USB connection.</summary>
    public bool IsUsb => Device.ConnectionType == ConnectionType.Usb;

    /// <summary>Whether the device needs a firmware update.</summary>
    public bool IsFirmwareOutdated => Device.IsFirmwareOutdated;

    /// <summary>Whether the device is currently connected.</summary>
    public bool IsConnected => Device.IsConnected;

    /// <summary>
    /// Type-coded stripe color — cyan for USB, purple for WiFi. Connection
    /// type is a meaningful dimension operators sort by, so it earns a
    /// dedicated hue.
    /// </summary>
    public Brush StripeBrush => IsUsb ? UsbAccent : WifiAccent;

    /// <summary>Background color for the tile.</summary>
    public Brush TileBackground => SurfaceRaised;

    /// <summary>Border color — stripe color when connected, dim otherwise.</summary>
    public Brush TileBorderBrush => IsConnected ? StripeBrush : BorderDim;

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
            case nameof(IStreamingDevice.DeviceSerialNo):
                OnPropertyChanged(nameof(SerialNumber));
                break;
            case nameof(IStreamingDevice.DeviceVersion):
                OnPropertyChanged(nameof(Version));
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

    private static readonly Brush SurfaceRaised = MakeBrush("#171A20");
    private static readonly Brush BorderDim = MakeBrush("#2A2F38");
    private static readonly Brush UsbAccent = MakeBrush("#06B6D4");
    private static readonly Brush WifiAccent = MakeBrush("#A855F7");

    private static SolidColorBrush MakeBrush(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex)!;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
