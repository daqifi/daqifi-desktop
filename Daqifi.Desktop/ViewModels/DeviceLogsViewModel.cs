using System.Collections.ObjectModel;
using Application = System.Windows.Application;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daqifi.Desktop.Device;
using System.Windows.Input;
using Daqifi.Desktop.Models;

namespace Daqifi.Desktop.ViewModels;

public partial class DeviceLogsViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyMessage;

    [ObservableProperty]
    private ObservableCollection<IStreamingDevice> _connectedDevices;

    [ObservableProperty]
    private IStreamingDevice _selectedDevice;

    private ObservableCollection<SdCardFile> _deviceFiles;

    public ObservableCollection<SdCardFile> DeviceFiles
    {
        get => _deviceFiles;
        set
        {
            if (SetProperty(ref _deviceFiles, value))
            {
                OnPropertyChanged(nameof(HasNoFiles));
            }
        }
    }

    [ObservableProperty]
    private bool _canRefreshFiles;

    public bool CanAccessSdCard => SelectedDevice?.ConnectionType == ConnectionType.Usb;

    public bool HasNoFiles => !DeviceFiles.Any() && CanAccessSdCard;

    public string ConnectionTypeMessage => SelectedDevice == null ? string.Empty :
        SelectedDevice.ConnectionType == ConnectionType.Usb ?
            "USB Connected - SD Card Access Available" :
            "WiFi Connected - SD Card Access Requires USB Connection";

    public ICommand RefreshFilesCommand { get; }

    public DeviceLogsViewModel()
    {
        ConnectedDevices = new ObservableCollection<IStreamingDevice>();
        DeviceFiles = new ObservableCollection<SdCardFile>();
        DeviceFiles.CollectionChanged += (s, e) => OnPropertyChanged(nameof(HasNoFiles));

        // Initialize commands
        RefreshFilesCommand = new RelayCommand(RefreshFiles, () => CanAccessSdCard);

        // Subscribe to device connection changes
        ConnectionManager.Instance.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == "ConnectedDevices")
            {
                UpdateConnectedDevices();
            }
        };

        // Initial load
        UpdateConnectedDevices();
    }

    private void UpdateConnectedDevices()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            ConnectedDevices.Clear();
            foreach (var device in ConnectionManager.Instance.ConnectedDevices)
            {
                ConnectedDevices.Add(device);
            }

            // If we have devices but none selected, select the first one
            if (SelectedDevice == null && ConnectedDevices.Any())
            {
                SelectedDevice = ConnectedDevices.First();
            }
        });
    }

    partial void OnSelectedDeviceChanged(IStreamingDevice value)
    {
        if (value != null)
        {
            // Only refresh files if we have USB access
            if (CanAccessSdCard)
            {
                RefreshFiles();
            }
            else
            {
                DeviceFiles.Clear();
            }
        }
        else
        {
            DeviceFiles.Clear();
        }

        OnPropertyChanged(nameof(CanAccessSdCard));
        OnPropertyChanged(nameof(ConnectionTypeMessage));
        (RefreshFilesCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private async void RefreshFiles()
    {
        if (SelectedDevice == null || !CanAccessSdCard)
        {
            return;
        }

        try
        {
            IsBusy = true;
            BusyMessage = "Refreshing files...";

            // Clear existing files
            DeviceFiles.Clear();

            // Request file list from device
            SelectedDevice.RefreshSdCardFiles();

            // Wait for a moment to let the device respond
            await Task.Delay(1000);

            // Update our list with any files found
            foreach (var file in SelectedDevice.SdCardFiles)
            {
                DeviceFiles.Add(file);
            }
        }
        catch (Exception ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                await ShowMessage("Error", $"Failed to refresh files: {ex.Message}", MessageDialogStyle.Affirmative);
            });
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
        }
    }

    private async Task ShowMessage(string title, string message, MessageDialogStyle dialogStyle)
    {
        var window = Application.Current.MainWindow as MetroWindow;
        await window.ShowMessageAsync(title, message, dialogStyle);
    }
}