using Daqifi.Desktop.DataModel.Device;
using Daqifi.Desktop.Device.SerialDevice;
using Daqifi.Desktop.Device.WiFiDevice;
using System.Globalization;
using CoreDeviceInfo = Daqifi.Core.Device.Discovery.IDeviceInfo;
using CoreConnectionType = Daqifi.Core.Device.Discovery.ConnectionType;

namespace Daqifi.Desktop.Device;

/// <summary>
/// Converts Core's IDeviceInfo to Desktop device objects
/// </summary>
public static class DeviceInfoConverter
{
    /// <summary>
    /// Converts Core IDeviceInfo to Desktop DeviceInfo datamodel
    /// </summary>
    public static DeviceInfo ToDesktopDeviceInfo(CoreDeviceInfo coreInfo)
    {
        return new DeviceInfo
        {
            DeviceName = coreInfo.Name,
            IpAddress = coreInfo.IPAddress?.ToString() ?? string.Empty,
            MacAddress = coreInfo.MacAddress ?? string.Empty,
            Port = (uint)(coreInfo.Port ?? 0),
            IsPowerOn = coreInfo.IsPowerOn,
            DeviceSerialNo = coreInfo.SerialNumber,
            DeviceVersion = coreInfo.FirmwareVersion
        };
    }

    /// <summary>
    /// Converts Core IDeviceInfo to Desktop DaqifiStreamingDevice
    /// </summary>
    public static DaqifiStreamingDevice ToWiFiDevice(CoreDeviceInfo coreInfo)
    {
        var deviceInfo = ToDesktopDeviceInfo(coreInfo);
        return new DaqifiStreamingDevice(deviceInfo);
    }

    /// <summary>
    /// Converts Core IDeviceInfo to Desktop SerialStreamingDevice
    /// </summary>
    public static SerialStreamingDevice ToSerialDevice(CoreDeviceInfo coreInfo)
    {
        if (string.IsNullOrEmpty(coreInfo.PortName))
        {
            throw new ArgumentException("PortName is required for serial devices", nameof(coreInfo));
        }

        return new SerialStreamingDevice(coreInfo.PortName);
    }
}
