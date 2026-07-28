using System.Collections.ObjectModel;
using System.Globalization;
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
    #region Constants
    /// <summary>
    /// How many skipped file names the completion dialog lists before collapsing the rest into a
    /// count. A card carrying dozens of empty logs would otherwise produce a dialog taller than
    /// the screen.
    /// </summary>
    private const int MAX_LISTED_SKIPPED_FILES = 5;
    #endregion

    private readonly IAppLogger _logger;

    /// <summary>
    /// Importer injected by tests. <c>null</c> in production, where each import resolves a fresh
    /// importer from the service provider (which is not available under unit test).
    /// </summary>
    private readonly ISdCardSessionImporter? _importerOverride;

    /// <summary>
    /// Where a freshly imported session is published, injected by tests. <c>null</c> in production,
    /// where it goes to <see cref="LoggingManager.Instance"/> — a singleton tests cannot touch,
    /// because its constructor resolves services from <see cref="App.ServiceProvider"/>.
    /// </summary>
    private readonly Action<LoggingSession>? _sessionSinkOverride;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyMessage = string.Empty;

    [ObservableProperty]
    private ObservableCollection<IStreamingDevice> _connectedDevices;

    [ObservableProperty]
    private IStreamingDevice? _selectedDevice;

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

    private ObservableCollection<SdCardFile> _deviceFiles = [];

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

    /// <summary>
    /// Initializes the ViewModel against the application's own logger and logging database, and
    /// begins tracking the connected-device list.
    /// </summary>
    public DeviceLogsViewModel()
        : this(null, null)
    {
    }

    /// <summary>
    /// Test seam: lets unit tests observe the log level a failure is reported at and drive the
    /// import path without the <see cref="App.ServiceProvider"/> / <see cref="Application.Current"/>
    /// singletons, neither of which exists under test.
    /// </summary>
    /// <param name="logger">Logger to report failures through.</param>
    /// <param name="importer">Importer to run imports with.</param>
    /// <param name="sessionSink">Where successfully imported sessions are published.</param>
    internal DeviceLogsViewModel(
        IAppLogger? logger,
        ISdCardSessionImporter? importer,
        Action<LoggingSession>? sessionSink = null)
    {
        _logger = logger ?? AppLogger.Instance;
        _importerOverride = importer;
        _sessionSinkOverride = sessionSink;

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

    private void OnDeviceFilesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
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

    partial void OnSelectedDeviceChanged(IStreamingDevice? value)
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
        // Invariant culture: this block is copied to the clipboard and pasted into bug reports, so it
        // must read the same regardless of the operator's locale.
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Device Serial: {SelectedDevice?.DeviceSerialNo ?? "N/A"}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Firmware Version: {SelectedDevice?.DeviceVersion ?? "N/A"}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Connection Type: {SelectedDevice?.ConnectionType}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"SD Card State: {SdCardState}");
        if (!string.IsNullOrEmpty(SdCardErrorMessage))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Error: {SdCardErrorMessage}");
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
        // Snapshot the selection: an import runs for many seconds on a background thread, and the
        // user can select another device or disconnect this one while it does. Re-reading
        // SelectedDevice inside the lambda would download from whatever is selected by then.
        var device = SelectedDevice;
        if (file == null || device is not { ConnectionType: ConnectionType.Usb }) return;

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
                importer.ImportFromDeviceAsync(device, file.FileName, null, progress, CancellationToken.None));

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
            var failure = HandleImportFailure(ex, file.FileName, device);
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
        // Snapshot the selection once for the whole batch — see ImportFile.
        var device = SelectedDevice;
        if (device is not { ConnectionType: ConnectionType.Usb } || DeviceFiles == null || !DeviceFiles.Any()) return;

        var filesToImport = DeviceFiles.ToList();
        var outcome = new ImportAllOutcome { TotalCount = filesToImport.Count };

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
                        importer.ImportFromDeviceAsync(device, file.FileName, null, progress, CancellationToken.None));

                    AddImportedSession(result.Session);

                    if (result.TimestampQuality.HasDegenerateTimeAxis)
                    {
                        outcome.TimestampWarningCount++;
                    }

                    outcome.ImportedCount++;
                }
                catch (Exception ex)
                {
                    var failure = HandleImportFailure(ex, file.FileName, device);

                    if (failure.IsCardUnavailable)
                    {
                        // The card itself is gone or wedged: every remaining file would fail the
                        // same way, each burning the same multi-second timeout. Stop here.
                        outcome.AbortingFailure = failure;
                        outcome.AbortedOnFile = file.FileName;
                        break;
                    }

                    // Anything else may be specific to this file — an empty log, one corrupt
                    // directory entry, a rejected command. Skip it and keep going: aborting here
                    // silently dropped every later healthy file, and because the device lists
                    // files in the same order every time, a retry stopped at the same file
                    // (issue #780).
                    outcome.RecordSkip(file.FileName, failure.Guidance);
                }
            }

            await ShowMessage("Import Complete", BuildImportAllSummary(outcome), MessageDialogStyle.Affirmative);
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
    /// Builds the text of the "Import Complete" dialog from what the batch actually did.
    ///
    /// Skipped files are reported as skipped, by name, with the advice that applies to them — not
    /// as a card-wide abort. Telling the user to power-cycle the device because one log file was
    /// empty sent them after a fault that was not there and hid the fact that every other file
    /// imported fine (issue #780).
    /// </summary>
    /// <param name="outcome">What the batch imported, skipped, and stopped at.</param>
    /// <returns>The message body shown in the completion dialog.</returns>
    internal static string BuildImportAllSummary(ImportAllOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        var message = new StringBuilder();
        message.Append(CultureInfo.CurrentCulture, $"Imported {outcome.ImportedCount} of {outcome.TotalCount} files.");

        if (outcome.SkippedFiles.Count > 0)
        {
            message.Append(CultureInfo.CurrentCulture, $"\n\nSkipped {outcome.SkippedFiles.Count} file(s).");
            message.Append(" You can retry any of them on their own from the file list.");

            foreach (var fileName in outcome.SkippedFiles.Take(MAX_LISTED_SKIPPED_FILES))
            {
                message.Append(CultureInfo.CurrentCulture, $"\n  • {fileName}");
            }

            var undisplayed = outcome.SkippedFiles.Count - MAX_LISTED_SKIPPED_FILES;
            if (undisplayed > 0)
            {
                message.Append(CultureInfo.CurrentCulture, $"\n  • ...and {undisplayed} more");
            }

            // One line per distinct reason: a card holding both an empty log and a corrupt one
            // needs both pieces of advice, but a card holding ten empty logs needs it once.
            foreach (var guidance in outcome.SkipGuidance)
            {
                message.Append(CultureInfo.CurrentCulture, $"\n\n{guidance}");
            }
        }

        if (outcome.AbortingFailure != null)
        {
            message.Append(CultureInfo.CurrentCulture,
                $"\n\nImport stopped at {outcome.AbortedOnFile}: {outcome.AbortingFailure.Guidance}");
        }

        if (outcome.TimestampWarningCount > 0)
        {
            message.Append(CultureInfo.CurrentCulture,
                $"\n\nWarning: {outcome.TimestampWarningCount} file(s) have missing or unusable per-sample");
            message.Append(" timestamps; their sessions' time axes may be flat or partially collapsed.");
        }

        return message.ToString();
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
    private void AddImportedSession(LoggingSession session)
    {
        void Publish()
        {
            if (_sessionSinkOverride != null)
            {
                _sessionSinkOverride(session);
                return;
            }

            LoggingManager.Instance.LoggingSessions.Add(session);
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(Publish);
        }
        else
        {
            Publish();
        }
    }

    /// <summary>
    /// Classifies a failed import, reflects it in the SD card status surface, and logs it at the
    /// severity the failure deserves.
    /// </summary>
    /// <param name="ex">The exception the import failed with.</param>
    /// <param name="fileName">The SD card file being imported.</param>
    /// <param name="device">
    /// The device the import ran against, captured when it started. The selection may have moved
    /// on since, so the on-screen state is only updated while it is still the selected device.
    /// </param>
    /// <returns>The classification, so the caller can build the user-facing message from it.</returns>
    private SdCardFailure HandleImportFailure(Exception ex, string fileName, IStreamingDevice device)
    {
        var failure = SdCardFailureClassifier.Classify(ex);

        // Only a card-wide device condition belongs on the SD card panel. That panel *replaces*
        // the file list, so showing a single file's failure there hides every healthy file and
        // takes away the per-file retry that skipping the file exists to leave available (issue
        // #780). An unexpected failure (a defect in the import pipeline, a database error) says
        // nothing about the card either. Both still reach the user through the import dialog.
        if (failure is { IsExpectedDeviceCondition: true, IsCardUnavailable: true }
            && ReferenceEquals(SelectedDevice, device))
        {
            ApplyFailureState(failure);
        }

        LogFailure(ex, failure, $"Error importing {fileName} from device {device.DeviceSerialNo}");
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

/// <summary>
/// What an "Import All" run did, in the terms its completion dialog reports. Accumulated as the
/// batch runs and handed to <see cref="DeviceLogsViewModel.BuildImportAllSummary"/>, which is
/// separated out so the wording can be unit-tested without a WPF dialog.
/// </summary>
internal sealed class ImportAllOutcome
{
    private readonly List<string> _skippedFiles = [];
    private readonly List<string> _skipGuidance = [];

    /// <summary>How many files the batch started with.</summary>
    public int TotalCount { get; init; }

    /// <summary>How many files imported successfully.</summary>
    public int ImportedCount { get; set; }

    /// <summary>How many imported sessions came out with an unusable time axis.</summary>
    public int TimestampWarningCount { get; set; }

    /// <summary>
    /// Files that failed for a reason that may be specific to them, in list order. The batch
    /// carried on past each of these, and the user can still retry them individually.
    /// </summary>
    public IReadOnlyList<string> SkippedFiles => _skippedFiles;

    /// <summary>
    /// The distinct guidance sentences the skipped files produced, in first-seen order. Deduped
    /// so ten empty logs read out their advice once rather than ten times.
    /// </summary>
    public IReadOnlyList<string> SkipGuidance => _skipGuidance;

    /// <summary>
    /// The card-wide failure that ended the batch early, or <c>null</c> if it ran to completion.
    /// </summary>
    public SdCardFailure? AbortingFailure { get; set; }

    /// <summary>The file the batch stopped at, set with <see cref="AbortingFailure"/>.</summary>
    public string? AbortedOnFile { get; set; }

    /// <summary>
    /// Records a file the batch skipped and carried on past.
    /// </summary>
    /// <param name="fileName">The file that failed.</param>
    /// <param name="guidance">What the user should do about it.</param>
    public void RecordSkip(string fileName, string guidance)
    {
        _skippedFiles.Add(fileName);

        if (!_skipGuidance.Contains(guidance))
        {
            _skipGuidance.Add(guidance);
        }
    }
}
