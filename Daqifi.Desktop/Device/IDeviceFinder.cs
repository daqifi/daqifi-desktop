namespace Daqifi.Desktop.Device;

public delegate void OnDeviceFoundHandler(object sender, IDevice device);
public delegate void OnDeviceRemovedHandler(object sender, IDevice device);

public interface IDeviceFinder
{
    event OnDeviceFoundHandler OnDeviceFound;
    event OnDeviceRemovedHandler OnDeviceRemoved;

    void Start();
    void Stop();
    void NotifyDeviceFound(object sender, IDevice device);
    void NotifyDeviceRemoved(object sender, IDevice device);
}