using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.Loggers;
using Daqifi.Desktop.Models;
using Daqifi.Core.Device.SdCard;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Daqifi.Desktop.ViewModels;

/// <summary>
/// Represents the current state of the SD card in the connected device.
/// </summary>
public enum SdCardState
{
    /// <summary>SD card state has not yet been determined.</summary>
    Unknown,
    /// <summary>SD card is present and accessible.</summary>
    Ok,
    /// <summary>No SD card is installed in the device.</summary>
    NotPresent,
    /// <summary>SD card is present but an error occurred accessing it.</summary>
    Error
}

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoFiles))]
    [NotifyPropertyChangedFor(nameof(HasFiles))]
    [NotifyPropertyChangedFor(nameof(HasSdCardNotPresent))]
    [NotifyPropertyChangedFor(nameof(HasSdCardError))]
    [NotifyPropertyChangedFor(nameof(SdCardStatusLine))]
    private SdCardState _sdCardState = SdCardState.Unknown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSdCardError))]
    [NotifyPropertyChangedFor(nameof(SdCardStatusLine))]
    private string _sdCardErrorMessage = string.Empty;

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
                OnPropertyChanged(nameof(HasFiles));
            }
        }
    }

    public bool CanAccessSdCard => SelectedDevice?.ConnectionType == ConnectionType.Usb;

    /// <summary>True when the USB device has an OK SD card but no log files on it.</summary>
    public bool HasNoFiles => (DeviceFiles?.Any() != true) && CanAccessSdCard && SdCardState == SdCardState.Ok;

    /// <summary>True when the USB device has an OK SD card with at least one log file.</summary>
    public bool HasFiles => CanAccessSdCard && (DeviceFiles?.Any() == true) && SdCardState == SdCardState.Ok;

    /// <summary>True when the USB device reports that no SD card is installed.</summary>
    public bool HasSdCardNotPresent => CanAccessSdCard && SdCardState == SdCardState.NotPresent;

    /// <summary>True when the USB device reports an SD card error.</summary>
    public bool HasSdCardError => CanAccessSdCard && SdCardState == SdCardState.Error;

    public string ConnectionTypeMessage => SelectedDevice == null ? string.Empty :
        SelectedDevice.ConnectionType == ConnectionType.Usb ?
            "USB Connected - SD Card Access Available" :
            "WiFi Connected - SD Card Access Requires USB Connection";

    /// <summary>
    /// Short status string appended to the connection status bar.
    /// Returns an empty string when the SD card state is unknown.
    /// </summary>
    public string SdCardStatusLine => SdCardState switch
    {
        SdCardState.Ok =>
            $" · SD card OK · {DeviceFiles?.Count ?? 0} {(DeviceFiles?.Count == 1 ? "file" : "files")}",
        SdCardState.NotPresent => " · No SD card installed",
        SdCardState.Error =>
            $" · SD card error{(!string.IsNullOrEmpty(SdCardErrorMessage) ? $": {SdCardErrorMessage}" : string.Empty)}",
        _ => string.Empty
    };

    /// <summary>Refreshes the SD card file list from the selected device.</summary>
    public IAsyncRelayCommand RefreshFilesCommand { get; }

    public DeviceLogsViewModel()
    {
        ConnectedDevices = new ObservableCollection<IStreamingDevice>();
        DeviceFiles = new ObservableCollection<SdCardFile>();
        DeviceFiles.CollectionChanged += OnDeviceFilesCollectionChanged;

        RefreshFilesCommand = new AsyncRelayCommand(RefreshFilesAsync, () => CanAccessSdCard);

        ConnectionManager.Instance.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == "ConnectedDevices")
            {
                UpdateConnectedDevices();
            }
        };

        UpdateConnectedDevices();
    }

    private void OnDeviceFilesCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasNoFiles));
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(SdCardStatusLine));
    }

    private void UpdateConnectedDevices()
    {
        void Update()
        {
            ConnectedDevices.Clear();
            foreach (var device in ConnectionManager.Instance.ConnectedDevices)
            {
                ConnectedDevices.Add(device);
            }

            if (SelectedDevice == null && ConnectedDevices.Any())
            {
                SelectedDevice = ConnectedDevices.First();
            }
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null)
        {
            dispatcher.Invoke(Update);
        }
        else
        {
            Update();
        }
    }

    partial void OnSelectedDeviceChanged(IStreamingDevice value)
    {
        SdCardState = SdCardState.Unknown;
        SdCardErrorMessage = string.Empty;

        if (value != null)
        {
            if (CanAccessSdCard)
            {
                _ = RefreshFilesAsync();
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
        OnPropertyChanged(nameof(HasSdCardNotPresent));
        OnPropertyChanged(nameof(HasSdCardError));
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(ConnectionTypeMessage));
        RefreshFilesCommand.NotifyCanExecuteChanged();
    }

    internal async Task RefreshFilesAsync()
    {
        var device = SelectedDevice;
        if (device == null || device.ConnectionType != ConnectionType.Usb)
        {
            return;
        }

        try
        {
            IsBusy = true;
            BusyMessage = "Refreshing files...";
            SdCardState = SdCardState.Unknown;
            SdCardErrorMessage = string.Empty;

            DeviceFiles.Clear();

            await Task.Run(() => device.RefreshSdCardFiles());

            if (SelectedDevice != device)
            {
                return;
            }

            foreach (var file in device.SdCardFiles)
            {
                DeviceFiles.Add(file);
            }

            SdCardState = SdCardState.Ok;
        }
        catch (SdCardNotPresentException ex)
        {
            SdCardState = SdCardState.NotPresent;
            SdCardErrorMessage = string.Empty;
            _logger.Warning($"SD card not present in device {device.DeviceSerialNo}: {ex.Message}");
        }
        catch (SdCardFilesystemException ex)
        {
            SdCardState = SdCardState.Error;
            SdCardErrorMessage = ex.DeviceMessage ?? ex.Message;
            _logger.Error(ex, "SD card filesystem error");
        }
        catch (SdCardOperationException ex)
        {
            SdCardState = SdCardState.Error;
            SdCardErrorMessage = ex.LastScpiError ?? ex.Message;
            _logger.Error(ex, "SD card operation error");
        }
        catch (Exception ex)
        {
            SdCardState = SdCardState.Error;
            SdCardErrorMessage = ex.Message;
            _logger.Error(ex, "Failed to refresh SD card files");
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
        }
    }

    [RelayCommand]
    private void CopyDiagnosticInfo()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Device Serial: {SelectedDevice?.DeviceSerialNo ?? "N/A"}");
        sb.AppendLine($"Firmware Version: {SelectedDevice?.DeviceVersion ?? "N/A"}");
        sb.AppendLine($"Connection Type: {SelectedDevice?.ConnectionType}");
        sb.AppendLine($"SD Card State: {SdCardState}");
        if (!string.IsNullOrEmpty(SdCardErrorMessage))
        {
            sb.AppendLine($"Error: {SdCardErrorMessage}");
        }

        try
        {
            Clipboard.SetText(sb.ToString());
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to copy diagnostic info to clipboard: {ex.Message}");
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

            var result = await Task.Run(() =>
                importer.ImportFromDeviceAsync(SelectedDevice, file.FileName, null, progress, CancellationToken.None));

            Application.Current.Dispatcher.Invoke(() =>
            {
                LoggingManager.Instance.LoggingSessions.Add(result.Session);
            });

            var message = $"Successfully imported {file.FileName}";
            var timestampWarning = result.TimestampQuality.BuildUserWarning();
            if (timestampWarning != null)
            {
                message += $"\n\nWarning: {timestampWarning}";
            }

            await ShowMessage("Import Complete", message, MessageDialogStyle.Affirmative);
        }
        catch (OperationCanceledException)
        {
            // User cancelled
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Error importing {file.FileName}");
            await ShowMessage("Import Failed",
                $"Failed to import {file.FileName}. Please check the device connection and try again.",
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
        var timestampWarningCount = 0;

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

                    var result = await Task.Run(() =>
                        importer.ImportFromDeviceAsync(SelectedDevice, file.FileName, null, progress, CancellationToken.None));

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        LoggingManager.Instance.LoggingSessions.Add(result.Session);
                    });

                    if (result.TimestampQuality.HasDegenerateTimeAxis)
                    {
                        timestampWarningCount++;
                    }

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

            if (timestampWarningCount > 0)
            {
                message += $"\nWarning: {timestampWarningCount} file(s) have missing or unusable per-sample " +
                           "timestamps; their sessions' time axes may be flat or partially collapsed.";
            }

            await ShowMessage("Import Complete", message, MessageDialogStyle.Affirmative);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error importing all files");
            await ShowMessage("Import Failed",
                "Import failed. Please check the device connection and try again.",
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
