using CommunityToolkit.Mvvm.ComponentModel;
using Daqifi.Desktop.Device;

namespace Daqifi.Desktop.ViewModels
{
    [ObservableObject]
    public partial class DeviceSettingsViewModel
    {
        [ObservableProperty]
        private IStreamingDevice _selectedDevice;

        [ObservableProperty]
        private bool _isLoggingToDevice;

        [ObservableProperty]
        private string _logFileName;

        public bool CanAccessSdCard => SelectedDevice?.ConnectionType == ConnectionType.Usb;
        
        public string SdCardMessage => GetSdCardMessage();

        partial void OnSelectedDeviceChanged(IStreamingDevice value)
        {
            // If switching to WiFi or no device, ensure logging is disabled
            if (value == null || value.ConnectionType != ConnectionType.Usb)
            {
                IsLoggingToDevice = false;
                LogFileName = string.Empty;
            }
        }

        partial void OnIsLoggingToDeviceChanging(bool value)
        {
            // Prevent enabling logging if not on USB
            if (value && (SelectedDevice == null || SelectedDevice.ConnectionType != ConnectionType.Usb))
            {
                throw new InvalidOperationException("Cannot enable logging when not connected via USB");
            }
        }

        private string GetSdCardMessage()
        {
            if (SelectedDevice == null)
            {
                return string.Empty;
            }
            
            return SelectedDevice.ConnectionType == ConnectionType.Usb
                ? "SD Card logging available"
                : "SD Card logging is not available on WiFi devices";
        }
    }
} 