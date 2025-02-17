namespace Daqifi.Desktop.Models
{
    /// <summary>
    /// Defines the available logging modes for the device
    /// </summary>
    public enum LoggingMode
    {
        /// <summary>
        /// Data is streamed directly to the application
        /// </summary>
        Stream,

        /// <summary>
        /// Data is logged to the device's SD card
        /// </summary>
        SdCard
    }
} 