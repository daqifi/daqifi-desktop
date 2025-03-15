using System.Collections.ObjectModel;
using Application = System.Windows.Application;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daqifi.Desktop.Device;
using System.Windows.Input;
using Daqifi.Desktop.Models;

namespace Daqifi.Desktop.ViewModels
{
    public partial class DeviceLogsViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _busyMessage;

        [ObservableProperty]
        private ObservableCollection<IStreamingDevice> _connectedDevices;

        [ObservableProperty]
        private IStreamingDevice _selectedDevice;

        [ObservableProperty]
        private ObservableCollection<SdCardFile> _deviceFiles;

        [ObservableProperty]
        private bool _canRefreshFiles;

        public bool CanAccessSdCard => SelectedDevice?.ConnectionType == ConnectionType.Usb;
        
        public string ConnectionTypeMessage => SelectedDevice == null ? string.Empty :
            SelectedDevice.ConnectionType == ConnectionType.Usb ? 
                "USB Connected - SD Card Access Available" : 
                "WiFi Connected - SD Card Access Requires USB Connection";

        public ICommand RefreshFilesCommand { get; }

        public DeviceLogsViewModel()
        {
            ConnectedDevices = new ObservableCollection<IStreamingDevice>();
            DeviceFiles = new ObservableCollection<SdCardFile>();
            
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

                // Wait for the device to respond (with a timeout)
                int timeoutMs = 5000;
                int elapsedMs = 0;
                int checkIntervalMs = 100;

                while (elapsedMs < timeoutMs)
                {
                    await Task.Delay(checkIntervalMs);
                    elapsedMs += checkIntervalMs;

                    // Check if we have files
                    if (SelectedDevice.SdCardFiles.Any())
                    {
                        // Update our list
                        foreach (var file in SelectedDevice.SdCardFiles)
                        {
                            DeviceFiles.Add(file);
                        }
                        break;
                    }
                }

                if (!DeviceFiles.Any() && elapsedMs >= timeoutMs)
                {
                    await Application.Current.Dispatcher.InvokeAsync(async () =>
                    {
                        await ShowMessage("No Files Found", "No files were found on the device or the device did not respond.", MessageDialogStyle.Affirmative);
                    });
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
} 