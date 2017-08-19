using Daqifi.Desktop;
using Daqifi.Desktop.Bootloader;
using DAQifi.Desktop.Device;
using ObservableObject = Daqifi.Desktop.ObservableObject;

namespace DAQifi.Desktop.ViewModels
{
    public class FirmwareDialogViewModel : ObservableObject
    {
        private string _version;
        private HidDevice _hidDevice;
        private Pic32Bootloader _bootloader;

        public string Version
        {
            get => _version;
            set
            {
                _version = value;
                NotifyPropertyChanged("Version");
            }
        }

        public FirmwareDialogViewModel(HidDevice hidDevice)
        {
            _hidDevice = hidDevice;
            _bootloader = new Pic32Bootloader();
            _bootloader.PropertyChanged += OnHidDevicePropertyChanged;
            _bootloader.RequestVersion();
        }

        private void OnHidDevicePropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Version")
            {
                Version = _bootloader.Version;
            }
        }
    }
}
