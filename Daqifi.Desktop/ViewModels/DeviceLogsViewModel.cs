using System.Collections.ObjectModel;
using Application = System.Windows.Application;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.Loggers;
using Daqifi.Desktop.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Input;

namespace Daqifi.Desktop.ViewModels;

public partial class DeviceLogsViewModel : ObservableObject
{
    private readonly AppLogger _logger = AppLogger.Instance;

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
            if (Equals(_deviceFiles, value)) return;

            if (_deviceFiles != null)
            {
                _deviceFiles.CollectionChanged -= OnDeviceFilesCollectionChanged;
            }

            if (SetProperty(ref _deviceFiles, value))
            {
                if (_deviceFiles != null)
                {
                    _deviceFiles.CollectionChanged += OnDeviceFilesCollectionChanged;
                }
                OnPropertyChanged(nameof(HasNoFiles));
            }
        }
    }

    [ObservableProperty]
    private bool _canRefreshFiles;

    public bool CanAccessSdCard => SelectedDevice?.ConnectionType == ConnectionType.Usb;

    public bool HasNoFiles => (DeviceFiles?.Any() != true) && CanAccessSdCard;

    public string ConnectionTypeMessage => SelectedDevice == null ? string.Empty :
        SelectedDevice.ConnectionType == ConnectionType.Usb ?
            "USB Connected - SD Card Access Available" :
            "WiFi Connected - SD Card Access Requires USB Connection";

    public ICommand RefreshFilesCommand { get; }

    public DeviceLogsViewModel()
    {
        ConnectedDevices = new ObservableCollection<IStreamingDevice>();
        DeviceFiles = new ObservableCollection<SdCardFile>();
        DeviceFiles.CollectionChanged += OnDeviceFilesCollectionChanged;

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

    private void OnDeviceFilesCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasNoFiles));
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
        OnPropertyChanged(nameof(HasNoFiles));
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

            // Request file list from device without blocking the UI thread
            await Task.Run(() => SelectedDevice.RefreshSdCardFiles());

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

    [RelayCommand]
    private async Task ImportFile(SdCardFile? file)
    {
        if (file == null || SelectedDevice == null || !CanAccessSdCard) return;

        try
        {
            IsBusy = true;
            BusyMessage = $"Downloading {file.FileName}...";

            var loggingContext = App.ServiceProvider.GetRequiredService<IDbContextFactory<LoggingContext>>();
            var importer = new SdCardSessionImporter(loggingContext);

            var progress = new Progress<ImportProgress>(p =>
            {
                BusyMessage = $"Importing {file.FileName}... {p.SamplesProcessed:N0} samples";
            });

            var session = await Task.Run(() =>
                importer.ImportFromDeviceAsync(SelectedDevice, file.FileName, null, progress, CancellationToken.None));

            Application.Current.Dispatcher.Invoke(() =>
            {
                LoggingManager.Instance.LoggingSessions.Add(session);
            });

            await ShowMessage("Import Complete",
                $"Successfully imported {file.FileName}",
                MessageDialogStyle.Affirmative);
        }
        catch (OperationCanceledException)
        {
            // User cancelled
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Error importing {file.FileName}");
            await ShowMessage("Import Failed",
                $"Failed to import {file.FileName}: {ex.Message}",
                MessageDialogStyle.Affirmative);
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
        }
    }

    [RelayCommand]
    private async Task ImportAllFiles()
    {
        if (SelectedDevice == null || !CanAccessSdCard || DeviceFiles == null || !DeviceFiles.Any()) return;

        var filesToImport = DeviceFiles.ToList();
        var successCount = 0;
        var failCount = 0;

        try
        {
            IsBusy = true;

            var loggingContext = App.ServiceProvider.GetRequiredService<IDbContextFactory<LoggingContext>>();
            var importer = new SdCardSessionImporter(loggingContext);

            for (var i = 0; i < filesToImport.Count; i++)
            {
                var file = filesToImport[i];
                BusyMessage = $"Importing file {i + 1} of {filesToImport.Count}: {file.FileName}...";

                try
                {
                    var progress = new Progress<ImportProgress>(p =>
                    {
                        BusyMessage = $"Importing {file.FileName} ({i + 1}/{filesToImport.Count})... {p.SamplesProcessed:N0} samples";
                    });

                    var session = await Task.Run(() =>
                        importer.ImportFromDeviceAsync(SelectedDevice, file.FileName, null, progress, CancellationToken.None));

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        LoggingManager.Instance.LoggingSessions.Add(session);
                    });

                    successCount++;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Error importing {file.FileName}");
                    failCount++;
                }
            }

            var message = $"Imported {successCount} of {filesToImport.Count} files.";
            if (failCount > 0)
            {
                message += $"\n{failCount} file(s) failed to import.";
            }

            await ShowMessage("Import Complete", message, MessageDialogStyle.Affirmative);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error importing all files");
            await ShowMessage("Import Failed",
                $"Import failed: {ex.Message}",
                MessageDialogStyle.Affirmative);
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
