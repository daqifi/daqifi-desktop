using Daqifi.Desktop.Bootloader;
using Daqifi.Desktop.Commands;
using Daqifi.Desktop.Loggers;
using System;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Device.HidDevice;
using ObservableObject = Daqifi.Desktop.ObservableObject;

namespace DAQifi.Desktop.ViewModels
{
    public class FirmwareDialogViewModel : ObservableObject
    {
        private string _version;
        private HidFirmwareDevice _hidFirmwareDevice;
        private Pic32Bootloader _bootloader;
        private string _firmwareFilePath;
        private bool _isFirmwareUploading;

        public string Version
        {
            get => _version;
            set
            {
                _version = value;
                NotifyPropertyChanged("Version");
            }
        }

        public string FirmwareFilePath
        {
            get => _firmwareFilePath;
            set
            {
                _firmwareFilePath = value;
                NotifyPropertyChanged("FirmwareFilePath");
            }
        }

        public bool IsFirmwareUploading
        {
            get => _isFirmwareUploading;
            set
            {
                _isFirmwareUploading = value;
                NotifyPropertyChanged("IsFirmwareUploading");
            }
        }

        public ICommand BrowseFirmwarePathCommand { get; private set; }
        private bool CanBrowseFirmwarePath(object o)
        {
            return true;
        }

        public ICommand UploadFirmwareCommand { get; private set; }
        private bool CanUploadFirmware(object o)
        {
            return true;
        }

        public FirmwareDialogViewModel(HidFirmwareDevice hidFirmwareDevice)
        {
            _hidFirmwareDevice = hidFirmwareDevice;
            _bootloader = new Pic32Bootloader(_hidFirmwareDevice.Device);
            _bootloader.PropertyChanged += OnHidDevicePropertyChanged;
            _bootloader.RequestVersion();

            BrowseFirmwarePathCommand = new DelegateCommand(BrowseFirmwarePath, CanBrowseFirmwarePath);
            UploadFirmwareCommand = new DelegateCommand(UploadFirmware, CanUploadFirmware);
        }

        private void OnHidDevicePropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Version")
            {
                Version = _bootloader.Version;
            }
        }

        private void BrowseFirmwarePath(object o)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                DefaultExt = ".hex",
                Filter = "Firmware|*.hex"
            };

            var result = dialog.ShowDialog();

            if (result == false) return;

            FirmwareFilePath = dialog.FileName;
        }

        private void UploadFirmware(object obj)
        {
            var bw = new BackgroundWorker();
            bw.DoWork += delegate
            {
                try
                {
                    IsFirmwareUploading = true;
                    if (string.IsNullOrWhiteSpace(FirmwareFilePath)) return;
                    if (!File.Exists(FirmwareFilePath)) return;

                    _bootloader.LoadFirmware(FirmwareFilePath);
                }
                catch (Exception ex)
                {
                    AppLogger.Instance.Error(ex, "Problem Uploading Firmware");
                }
                finally
                {
                    IsFirmwareUploading = false;
                }
            };

            bw.RunWorkerAsync();
        }
    }
}
