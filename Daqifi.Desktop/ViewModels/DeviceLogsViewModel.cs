using System.Collections.ObjectModel;
using Application = System.Windows.Application;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using Daqifi.Desktop.Device;
using System.Windows.Input;
using Daqifi.Desktop.Commands;
using Daqifi.Desktop.Models;
using Daqifi.Desktop.IO.Messages.Producers;

namespace Daqifi.Desktop.ViewModels
{
    public partial class DeviceLogsViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _busyMessage;

        [ObservableProperty]
        private double _progress;

        [ObservableProperty]
        private ObservableCollection<IStreamingDevice> _connectedDevices;

        [ObservableProperty]
        private IStreamingDevice _selectedDevice;

        [ObservableProperty]
        private ObservableCollection<SdCardFile> _deviceFiles;

        [ObservableProperty]
        private SdCardFile _selectedFile;

        public ICommand RefreshFilesCommand { get; private set; }
        public ICommand DownloadFileCommand { get; private set; }

        public DeviceLogsViewModel()
        {
            ConnectedDevices = new ObservableCollection<IStreamingDevice>();
            DeviceFiles = new ObservableCollection<SdCardFile>();
            
            // Initialize commands
            RefreshFilesCommand = new DelegateCommand(o => RefreshFiles());
            DownloadFileCommand = new DelegateCommand(o => DownloadFile(), o => CanDownloadFile());

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
                RefreshFiles();
            }
            else
            {
                DeviceFiles.Clear();
            }
        }

        private async void RefreshFiles()
        {
            if (SelectedDevice == null) return;

            try
            {
                IsBusy = true;
                BusyMessage = "Refreshing device files...";

                // Switch to SD card mode
                SelectedDevice.MessageProducer.Send(ScpiMessageProducer.DisableLan);
                await Task.Delay(100); // Give device time to process
                SelectedDevice.MessageProducer.Send(ScpiMessageProducer.EnableSdCard);
                await Task.Delay(100);

                // Request file list
                SelectedDevice.RefreshSdCardFiles();
                await Task.Delay(1000); // Wait for response

                // Update UI
                Application.Current.Dispatcher.Invoke(() =>
                {
                    DeviceFiles.Clear();
                    foreach (var file in SelectedDevice.SdCardFiles)
                    {
                        DeviceFiles.Add(file);
                    }
                });
            }
            catch (Exception ex)
            {
                await ShowMessage("Error", $"Failed to refresh device files: {ex.Message}", MessageDialogStyle.Affirmative);
            }
            finally
            {
                IsBusy = false;
                BusyMessage = string.Empty;
            }
        }

        private async void DownloadFile()
        {
            if (SelectedDevice == null || SelectedFile == null) return;

            try
            {
                IsBusy = true;
                BusyMessage = "Downloading file...";

                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = SelectedFile.FileName,
                    Filter = "All files (*.*)|*.*"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    SelectedDevice.DownloadSdCardFile(SelectedFile.FileName);
                    await Task.Delay(1000); // Wait for response

                    await ShowMessage("Success", "File downloaded successfully!", MessageDialogStyle.Affirmative);
                }
            }
            catch (Exception ex)
            {
                await ShowMessage("Error", $"Failed to download file: {ex.Message}", MessageDialogStyle.Affirmative);
            }
            finally
            {
                IsBusy = false;
                BusyMessage = string.Empty;
            }
        }

        private bool CanDownloadFile()
        {
            return SelectedDevice != null && SelectedFile != null;
        }

        private async Task<MessageDialogResult> ShowMessage(string title, string message, MessageDialogStyle dialogStyle)
        {
            var metroWindow = Application.Current.MainWindow as MetroWindow;
            return await metroWindow.ShowMessageAsync(title, message, dialogStyle, metroWindow.MetroDialogOptions);
        }
    }
} 