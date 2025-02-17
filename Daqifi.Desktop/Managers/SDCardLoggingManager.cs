using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.IO.Messages.Producers;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.Models;

namespace Daqifi.Desktop.Managers
{
    public class SDCardLoggingManager
    {
        private static readonly Lazy<SDCardLoggingManager> _instance = new(() => new SDCardLoggingManager());
        private readonly AppLogger _appLogger = AppLogger.Instance;
        private readonly Dictionary<string, bool> _loggingStates = new();
        private readonly Dictionary<string, List<SdCardFile>> _deviceFiles = new();

        public static SDCardLoggingManager Instance => _instance.Value;

        private SDCardLoggingManager() { }

        /// <summary>
        /// Enables SD card logging for a device, ensuring WiFi streaming is stopped first
        /// </summary>
        public async Task<bool> EnableLogging(IStreamingDevice device)
        {
            if (device == null) return false;

            try
            {
                // Stop WiFi streaming if active
                if (LoggingManager.Instance.Active)
                {
                    LoggingManager.Instance.Active = false;
                }

                // Enable SD card logging
                device.MessageProducer.Send(ScpiMessagePoducer.EnableSdLogging);
                _loggingStates[device.DeviceSerialNo] = true;
                
                _appLogger.Information($"Enabled SD card logging for device {device.DeviceSerialNo}");
                return true;
            }
            catch (Exception ex)
            {
                _appLogger.Error(ex, $"Failed to enable SD card logging for device {device?.DeviceSerialNo}");
                return false;
            }
        }

        /// <summary>
        /// Disables SD card logging for a device
        /// </summary>
        public void DisableLogging(IStreamingDevice device)
        {
            if (device == null) return;

            try
            {
                device.MessageProducer.Send(ScpiMessagePoducer.DisableSdLogging);
                _loggingStates[device.DeviceSerialNo] = false;
                
                _appLogger.Information($"Disabled SD card logging for device {device.DeviceSerialNo}");
            }
            catch (Exception ex)
            {
                _appLogger.Error(ex, $"Failed to disable SD card logging for device {device?.DeviceSerialNo}");
            }
        }

        /// <summary>
        /// Checks if SD card logging is enabled for a device
        /// </summary>
        public bool IsLoggingEnabled(string deviceSerialNo)
        {
            return _loggingStates.TryGetValue(deviceSerialNo, out var isEnabled) && isEnabled;
        }

        /// <summary>
        /// Updates the file list for a device
        /// </summary>
        public void UpdateFileList(string deviceSerialNo, List<SdCardFile> files)
        {
            if (string.IsNullOrEmpty(deviceSerialNo)) return;
            _deviceFiles[deviceSerialNo] = files;
        }

        /// <summary>
        /// Gets the file list for a device
        /// </summary>
        public List<SdCardFile> GetFileList(string deviceSerialNo)
        {
            return _deviceFiles.TryGetValue(deviceSerialNo, out var files) ? files : new List<SdCardFile>();
        }

        /// <summary>
        /// Validates if a device can enable SD card logging
        /// </summary>
        public (bool canEnable, string message) ValidateLoggingState(IStreamingDevice device)
        {
            if (device == null)
            {
                return (false, "No device selected.");
            }

            if (LoggingManager.Instance.Active)
            {
                return (false, "Cannot enable SD card logging while WiFi streaming is active. Please stop streaming first.");
            }

            return (true, null);
        }

        /// <summary>
        /// Clears all stored state for a device (used when device disconnects)
        /// </summary>
        public void ClearDeviceState(string deviceSerialNo)
        {
            _loggingStates.Remove(deviceSerialNo);
            _deviceFiles.Remove(deviceSerialNo);
        }
    }
} 