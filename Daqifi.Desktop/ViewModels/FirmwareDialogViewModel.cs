using Daqifi.Desktop;
using Daqifi.Desktop.Bootloader;
using DAQifi.Desktop.Device;

namespace DAQifi.Desktop.ViewModels
{
    public class FirmwareDialogViewModel : ObservableObject
    {
        private string _version;
        private HidDevice _hidDevice;

        public string Version
        {
            get => _version;
            set
            {
                _version = value;
                NotifyPropertyChanged(Version);
            }
        }

        public FirmwareDialogViewModel(HidDevice hidDevice)
        {
            _hidDevice = hidDevice;
            var bootloader = new Pic32Bootloader();
            Version = bootloader.GetVersion();
        }
    }
}
