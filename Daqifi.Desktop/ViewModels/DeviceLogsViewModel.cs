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
    private readonly IAppLogger _logger;

    /// <summary>
    /// Importer injected by tests. <c>null</c> in production, where each import resolves a fresh
    /// importer from the service provider (which is not available under unit test).
    /// </summary>
    private readonly ISdCardSessionImporter? _importerOverride;

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

    /// <summary>
    /// The actionable sentence shown under the SD card error panel. Varies by failure — a wedged
    /// SD subsystem needs a power cycle, a bad filesystem needs a reformat — so the view binds it
    /// instead of hard-coding one piece of advice for every error.
    /// </summary>
    [ObservableProperty]
    private string _sdCardErrorGuidance = SdCardFailureClassifier.GENERIC_CARD_GUIDANCE;

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

    /// <summary>
    /// The auto-refresh kicked off when a USB device is selected. Production code fires and
    /// forgets it; tests await it so a late-landing refresh cannot overwrite the state they are
    /// asserting on. <c>null</c> until a USB device has been selected.
    /// </summary>
    internal Task? InitialRefreshTask { get; private set; }

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

    public DeviceLogsViewModel()
        : this(null, null)
    {
    }

    /// <summary>
    /// Test seam: lets unit tests observe the log level a failure is reported at and drive the
    /// import path without the <see cref="App.ServiceProvider"/> / <see cref="Application.Current"/>
    /// singletons, neither of which exists under test.
    /// </summary>
    internal DeviceLogsViewModel(IAppLogger? logger, ISdCardSessionImporter? importer)
    {
        _logger = logger ?? AppLogger.Instance;
        _importerOverride = importer;

        ConnectedDevices = new ObservableCollection<IStreamingDevice>();
        DeviceFiles = new ObservableCollection<SdCardFile>();
        DeviceFiles.CollectionChanged += OnDeviceFilesCollectionChanged;

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
                InitialRefreshTask = RefreshFilesAsync();
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

    /// <summary>
    /// Refreshes the SD card file list from the selected device. Backs the generated
    /// <c>RefreshFilesCommand</c>, which is enabled only while <see cref="CanAccessSdCard"/> is true.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAccessSdCard))]
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
        catch (Exception ex)
        {
            // Every refresh failure lands the card in a non-Ok state, including the unexpected
            // ones: the file list on screen is stale either way.
            var failure = SdCardFailureClassifier.Classify(ex);
            ApplyFailureState(failure);
            LogFailure(ex, failure, $"Failed to refresh SD card files on device {device.DeviceSerialNo}");
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

            var importer = ResolveImporter();

            var progress = new Progress<ImportProgress>(p =>
            {
                BusyMessage = $"Importing {file.FileName}... {p.SamplesProcessed:N0} samples";
            });

            var result = await Task.Run(() =>
                importer.ImportFromDeviceAsync(SelectedDevice, file.FileName, null, progress, CancellationToken.None));

            AddImportedSession(result.Session);

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
            var failure = HandleImportFailure(ex, file.FileName);
            await ShowMessage("Import Failed",
                $"Could not import {file.FileName}.\n\n{failure.Guidance}",
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

        SdCardFailure? abortingFailure = null;

        try
        {
            IsBusy = true;

            var importer = ResolveImporter();

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

                    AddImportedSession(result.Session);

                    if (result.TimestampQuality.HasDegenerateTimeAxis)
                    {
                        timestampWarningCount++;
                    }

                    successCount++;
                }
                catch (Exception ex)
                {
                    var failure = HandleImportFailure(ex, file.FileName);
                    failCount++;

                    if (failure.IsCardUnavailable)
                    {
                        // The card itself is gone or wedged: every remaining file would fail the
                        // same way, each burning the same multi-second timeout. Stop here.
                        abortingFailure = failure;
                        break;
                    }
                }
            }

            var message = $"Imported {successCount} of {filesToImport.Count} files.";
            if (failCount > 0)
            {
                message += $"\n{failCount} file(s) failed to import.";
            }

            if (abortingFailure != null)
            {
                message += $"\n\nImport stopped early: {abortingFailure.Guidance}";
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

    /// <summary>
    /// Resolves the importer to use: the injected test double when present, otherwise a fresh
    /// importer over the application's logging database.
    /// </summary>
    private ISdCardSessionImporter ResolveImporter()
    {
        if (_importerOverride != null)
        {
            return _importerOverride;
        }

        var loggingContext = App.ServiceProvider.GetRequiredService<IDbContextFactory<LoggingContext>>();
        return new SdCardSessionImporter(loggingContext);
    }

    /// <summary>
    /// Publishes a freshly imported session to the session list on the UI thread.
    /// </summary>
    private static void AddImportedSession(LoggingSession session)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => LoggingManager.Instance.LoggingSessions.Add(session));
        }
        else
        {
            LoggingManager.Instance.LoggingSessions.Add(session);
        }
    }

    /// <summary>
    /// Classifies a failed import, reflects it in the SD card status surface, and logs it at the
    /// severity the failure deserves.
    /// </summary>
    /// <returns>The classification, so the caller can build the user-facing message from it.</returns>
    private SdCardFailure HandleImportFailure(Exception ex, string fileName)
    {
        var failure = SdCardFailureClassifier.Classify(ex);

        // Only an expected device condition tells us anything about the card. An unexpected
        // failure (a defect in the import pipeline, a database error) must not blame the card and
        // hide a perfectly good file list behind an error panel.
        if (failure.IsExpectedDeviceCondition)
        {
            ApplyFailureState(failure);
        }

        LogFailure(ex, failure, $"Error importing {fileName} from device {SelectedDevice?.DeviceSerialNo}");
        return failure;
    }

    /// <summary>
    /// Reflects a classified failure in the properties the SD card panel and status line bind to.
    /// </summary>
    private void ApplyFailureState(SdCardFailure failure)
    {
        SdCardState = failure.State;
        SdCardErrorMessage = failure.StatusMessage;
        SdCardErrorGuidance = failure.Guidance;
    }

    /// <summary>
    /// Logs a classified failure: Warning (local log only) for expected device conditions, Error
    /// (captured to Sentry) for everything else. Keeping expected conditions off the Error path is
    /// what stopped a wedged SD card from filing issues like #754.
    /// </summary>
    private void LogFailure(Exception ex, SdCardFailure failure, string context)
    {
        if (failure.IsExpectedDeviceCondition)
        {
            _logger.Warning(ex, $"{context}: {failure.Guidance}");
        }
        else
        {
            _logger.Error(ex, context);
        }
    }

    private static async Task ShowMessage(string title, string message, MessageDialogStyle dialogStyle)
    {
        // Null under unit test (no WPF Application) — and there is no user to show a dialog to.
        if (Application.Current?.MainWindow is not MetroWindow window)
        {
            return;
        }

        await window.ShowMessageAsync(title, message, dialogStyle);
    }
}
