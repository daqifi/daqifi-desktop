using Daqifi.Desktop.Device;
using System.Threading.Tasks;

namespace Daqifi.Desktop.Services.DeviceLogImport
{
    /// <summary>
    /// Service responsible for importing device logs into application logs
    /// </summary>
    public interface IDeviceLogImportService
    {
        /// <summary>
        /// Imports a device log file into the application logging session
        /// </summary>
        /// <param name="device">The device containing the log file</param>
        /// <param name="fileName">The name of the log file to import</param>
        /// <param name="progressCallback">Optional callback to report import progress</param>
        /// <returns>True if import was successful, false otherwise</returns>
        Task<bool> ImportDeviceLog(IStreamingDevice device, string fileName, IProgress<double> progressCallback = null);

        /// <summary>
        /// Cancels any ongoing import operation
        /// </summary>
        void CancelImport();
    }
} 