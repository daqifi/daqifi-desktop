using Daqifi.Desktop.Bootloader;
using Daqifi.Desktop.Commands;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device.HidDevice;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DAQifi.Desktop.ViewModels;

public class FirmwareDialogViewModel : ObservableObject
{
    private readonly Pic32Bootloader _bootloader;
    private string _version;
    private string _firmwareFilePath;
    private bool _isFirmwareUploading;
    private bool _isUploadComplete;
    private bool _hasErrorOccured;
    private int _uploadFirmwareProgress;

    public string Version
    {
        get => _version;
        set
        {
            _version = value;
            OnPropertyChanged(nameof(Version));
        }
    }

    public string FirmwareFilePath
    {
        get => _firmwareFilePath;
        set
        {
            _firmwareFilePath = value;
            OnPropertyChanged();
        }
    }

    public bool IsFirmwareUploading
    {
        get => _isFirmwareUploading;
        set
        {
            _isFirmwareUploading = value;
            OnPropertyChanged();
        }
    }

    public bool IsUploadComplete
    {
        get => _isUploadComplete;
        set
        {
            _isUploadComplete = value;
            OnPropertyChanged();
        }
    }

    public bool HasErrorOccured
    {
        get => _hasErrorOccured;
        set
        {
            _hasErrorOccured = value;
            OnPropertyChanged();
        }
    }

    public int UploadFirmwareProgress
    {
        get => _uploadFirmwareProgress;
        set
        {
            _uploadFirmwareProgress = value;
            OnPropertyChanged();
            OnPropertyChanged("UploadFirmwareProgressText");
        }
    }

    public string UploadFirmwareProgressText => ($"Upload Progress: {UploadFirmwareProgress}%");

    public ICommand BrowseFirmwarePathCommand { get; }
    private bool CanBrowseFirmwarePath(object o)
    {
        return true;
    }

    public ICommand UploadFirmwareCommand { get; }
    private bool CanUploadFirmware(object o)
    {
        return true;
    }

    public FirmwareDialogViewModel(HidFirmwareDevice hidFirmwareDevice)
    {
        _bootloader = new Pic32Bootloader(hidFirmwareDevice.Device);
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

        if (result == false) { return; }

        FirmwareFilePath = dialog.FileName;
    }

    private void UploadFirmware(object obj)
    {
        var bw = new BackgroundWorker();
        bw.DoWork += delegate
        {
            IsFirmwareUploading = true;
            if (string.IsNullOrWhiteSpace(FirmwareFilePath)) { return; }
            if (!File.Exists(FirmwareFilePath))
            {
                return;
            }

            _bootloader.LoadFirmware(FirmwareFilePath, bw);
        };
        bw.WorkerReportsProgress = true;
        bw.ProgressChanged += UploadFirmwareProgressChanged;
        bw.RunWorkerCompleted += HandleUploadCompleted;
        bw.RunWorkerAsync();

    }

    void UploadFirmwareProgressChanged(object sender, ProgressChangedEventArgs e)
    {
        UploadFirmwareProgress = e.ProgressPercentage;
    }

    private void HandleUploadCompleted(object sender, RunWorkerCompletedEventArgs e)
    {
        IsFirmwareUploading = false;
        if (e.Error != null)
        {
            AppLogger.Instance.Error(e.Error, "Problem Uploading Firmware");
            HasErrorOccured = true;
        }
        else
        {
            IsUploadComplete = true;
        }
    }
}