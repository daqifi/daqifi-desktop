using System.ComponentModel;

namespace Daqifi.Desktop.Device;

public interface IDevice : INotifyPropertyChanged
{
    int Id { get; set; }

    string Name { get; set; }

    /// <summary>
    /// Connects to the streamingDevice.
    /// </summary>
    /// <returns>True if successfully connected</returns>
    bool Connect();

    /// <summary>
    /// Disconnects from the streamingDevice
    /// </summary>
    /// <returns>True if successfully disconnected</returns>
    bool Disconnect();

    /// <summary>
    /// Reboots the streamingDevice
    /// </summary>
    void Reboot();

    /// <summary>
    /// Raised when the device's connection drops unexpectedly (not via an explicit
    /// <see cref="Disconnect"/> call) — e.g. reboot, unplug, WiFi/TCP drop, or
    /// firmware-flash re-enumeration. Subscribers should tear down their reference to this
    /// device and inform the user; the wrapper's own state is already updated by the time
    /// this fires.
    /// </summary>
    event EventHandler<ConnectionLostEventArgs>? ConnectionLost;
}