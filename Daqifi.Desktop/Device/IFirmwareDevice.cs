
namespace Daqifi.Desktop.Device;

public interface IFirmwareDevice : IDevice
{
    /// <summary>
    /// Updates Firmware of the device
    /// </summary>
    /// <param name="data"></param>
    void UpdateFirmware(byte[] data);
}