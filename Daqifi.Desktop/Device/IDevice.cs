using System.ComponentModel;

namespace Daqifi.Desktop.Device
{
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
    }
}
