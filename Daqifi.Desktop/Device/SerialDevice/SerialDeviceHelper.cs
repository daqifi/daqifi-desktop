using System.Management;

namespace Daqifi.Desktop.Device.SerialDevice;

public static class SerialDeviceHelper
{
    public static string[] GetAvailableDaqifiPorts()
    {
        var portNames = new List<string>();

        ManagementObjectCollection collection;
        using (var searcher = new ManagementObjectSearcher("Select * From WIN32_SerialPort"))
            collection = searcher.Get();

        foreach (var serialDevice in collection)
        {
            var deviceId = serialDevice.GetPropertyValue("DeviceID");
            var pnpDeviceId = serialDevice.GetPropertyValue("PNPDeviceID");

            var device = UsbDevice.Get((string)pnpDeviceId);
            if (device == null)
            {
                continue; // Skip devices that don't have USB device info
            }

            var deviceReportedDescription = device.BusReportedDeviceDescription;

            if (deviceReportedDescription != null && deviceReportedDescription.ToLower() == "nyquist")
            {
                portNames.Add((string)deviceId);
            }
        }
        return portNames.ToArray();
    }
}