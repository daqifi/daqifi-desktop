namespace Daqifi.Desktop.Bootloader
{
    /// <summary>
    /// Defines the minimal contract needed for a device that can receive firmware updates
    /// </summary>
    public interface IFirmwareUpdateDevice
    {
        /// <summary>
        /// Gets the device name/port (e.g. COM3)
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Connects to the device
        /// </summary>
        bool Connect();

        /// <summary>
        /// Disconnects from the device
        /// </summary>
        bool Disconnect();

        /// <summary>
        /// Enables LAN update mode for WiFi firmware updates
        /// </summary>
        void EnableLanUpdateMode();

        /// <summary>
        /// Resets the LAN after a WiFi firmware update
        /// </summary>
        void ResetLanAfterUpdate();

        /// <summary>
        /// Reboots the device
        /// </summary>
        void Reboot();
    }
} 