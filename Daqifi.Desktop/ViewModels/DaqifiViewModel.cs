using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Configuration;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.DiskSpace;
using Daqifi.Desktop.Helpers;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.Loggers;
using Daqifi.Desktop.Models;
using Daqifi.Desktop.UpdateVersion;
using Daqifi.Desktop.View;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Daqifi.Core.Firmware;
using Daqifi.Core.Communication.Transport;
using Daqifi.Desktop.Device.Firmware;
using Microsoft.Data.Sqlite;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Daqifi.Desktop.Device.SerialDevice;
using System.IO.Ports;
using Application = System.Windows.Application;
using File = System.IO.File;
using CommunityToolkit.Mvvm.Input;
using Daqifi.Core.Device.SdCard;

namespace Daqifi.Desktop.ViewModels;

public partial class DaqifiViewModel : ObservableObject
{
    private const int WifiChipInfoMaxAttempts = 3;
    private static readonly TimeSpan WifiChipInfoRetryDelay = TimeSpan.FromSeconds(2);

    private readonly AppLogger _appLogger = AppLogger.Instance;

    #region Private Variables
    private const int SidePanelWidth = 85;
    private const int TopToolbarHeight = 30;

    [ObservableProperty]
    private bool _isBusy;
    [ObservableProperty]
    private bool _isLoggedDataBusy;
    [ObservableProperty]
    private bool _isDeviceSettingsOpen;
    [ObservableProperty]
    private bool _isNotificationsOpen;
    [ObservableProperty]
    private bool _isFirmwareUpdatationFlyoutOpen;
    [ObservableProperty]
    private bool _isLogSummaryOpen;
    [ObservableProperty]
    private bool _isLoggingSessionSettingsOpen;
    [ObservableProperty]
    private bool _isLiveGraphSettingsOpen;
    [ObservableProperty]
    private bool _networkSettingsApplied;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNetworkSettingsError))]
    private string? _networkSettingsError;

    /// <summary>
    /// True when <see cref="NetworkSettingsError"/> has a message to display.
    /// Used to toggle visibility of the inline error row in the Devices drawer.
    /// </summary>
    public bool HasNetworkSettingsError => !string.IsNullOrEmpty(NetworkSettingsError);
    [ObservableProperty]
    private bool _isAppSettingsOpen;

    // In-pane confirm dialog state (used by ShowConfirm for delete confirmations, etc.).
    // Bound by the LoggedDataPane confirm overlay; replaces the MahApps MessageDialog
    // (white card / blue theme) which clashed with the dark, tile-based design system.

    /// <summary>True while the in-pane confirm overlay is visible.</summary>
    [ObservableProperty] private bool _isConfirmOpen;

    /// <summary>Title shown at the top of the confirm overlay card.</summary>
    [ObservableProperty] private string _confirmTitle = string.Empty;

    /// <summary>Body message shown in the confirm overlay card.</summary>
    [ObservableProperty] private string _confirmMessage = string.Empty;

    /// <summary>Label shown on the affirmative button of the confirm overlay (e.g. "DELETE").</summary>
    [ObservableProperty] private string _confirmAffirmativeLabel = "OK";

    /// <summary>
    /// When true, the confirm overlay's affirmative button uses the danger style
    /// (red outline) instead of the accent style (filled blue). Set by destructive
    /// callers of <see cref="ShowConfirm"/>.
    /// </summary>
    [ObservableProperty] private bool _confirmAffirmativeIsDestructive;

    private TaskCompletionSource<bool>? _confirmTcs;

    private SettingsViewModel? _appSettings;

    /// <summary>
    /// Lazily-constructed view model backing the app settings drawer. Deferring
    /// construction avoids touching <see cref="DaqifiSettings.Instance"/> (which
    /// performs filesystem IO in its constructor) until the drawer is opened.
    /// </summary>
    public SettingsViewModel AppSettings => _appSettings ??= new SettingsViewModel();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FlyoutWidth))]
    private int _width = 800;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FlyoutHeight))]
    private int _height = 600;
    private int _selectedIndex;
    private int _selectedStreamingFrequency;
    private WindowState _viewWindowState;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private IStreamingDevice? _selectedDevice;

    private VersionNotification? _versionNotification;
    [ObservableProperty]
    private LoggingSession _selectedLoggingSession;
    private bool _isLogging;

    [ObservableProperty]
    private bool _isDebugModeEnabled;

    [ObservableProperty]
    private DebugDataCollection _debugData = new();
    private bool _canToggleLogging;
    [ObservableProperty]
    private string _loggedDataBusyReason;
    [ObservableProperty]
    private string _firmwareFilePath;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelFirmwareUploadCommand))]
    private bool _isFirmwareUploading;
    [ObservableProperty]
    private bool _isUploadComplete;
    [ObservableProperty]
    private bool _hasErrorOccured;
    [ObservableProperty]
    private int _uploadFirmwareProgress;
    [ObservableProperty]
    private int _uploadWiFiProgress;
    [ObservableProperty]
    private string _firmwareUpdateStatusText = string.Empty;
    [ObservableProperty]
    private bool _selectedDeviceSupportsFirmwareUpdate;
    private readonly IFirmwareUpdateService _firmwareUpdateService;
    private readonly IFirmwareDownloadService _firmwareDownloadService;
    private readonly IDbContextFactory<LoggingContext>? _loggingContextFactory;
    private readonly Func<string, string, IFirmwareUpdateService> _wifiFirmwareUpdateServiceFactory;
    private CancellationTokenSource? _firmwareUploadCts;
    private ConnectionDialogViewModel _connectionDialogViewModel;
    private string _selectedLoggingMode = "Stream to App";
    private bool _isLogToDeviceMode;
    private SdCardLogFormat _selectedSdCardLogFormat = SdCardLogFormat.Protobuf;
    private IStreamingDevice? _deviceBeingUpdated;
    private IDiskSpaceMonitor? _diskSpaceMonitor;
    private ObservableCollection<LoggingSession>? _observedLoggingSessions;
    private CancellationTokenSource? _networkSettingsAppliedCts;
    private DispatcherTimer? _sdLoggingElapsedTimer;
    private DateTime? _sdLoggingStartedAt;

    /// <summary>
    /// Elapsed time since SD-card logging started in this session, formatted as HH:mm:ss.
    /// Driven by a 1Hz DispatcherTimer that runs only while <see cref="IsSdCardLoggingActive"/>.
    /// </summary>
    [ObservableProperty]
    private string _sdLoggingElapsed = "00:00:00";
    #endregion

    #region Properties

    private readonly ObservableCollection<Profile> _fallbackProfiles = [];
    private readonly ObservableCollection<LoggingSession> _fallbackLoggingSessions = [];

    public ObservableCollection<IStreamingDevice> ConnectedDevices { get; } = [];
    public ObservableCollection<Profile> Profiles => TryGetLoggingManager()?.SubscribedProfiles ?? _fallbackProfiles;

    public ObservableCollection<Notifications> NotificationList { get; } = [];
    public ObservableCollection<IChannel> ActiveChannels { get; } = [];
    public ObservableCollection<IChannel> ActiveInputChannels { get; } = [];
    public ObservableCollection<LoggingSession> LoggingSessions => TryGetLoggingManager()?.LoggingSessions ?? _fallbackLoggingSessions;
    public bool HasLoggingSessions => LoggingSessions.Count > 0;

    public PlotLogger Plotter { get; private set; }
    public DatabaseLogger DbLogger { get; private set; }
    public SummaryLogger SummaryLogger { get; private set; }

    /// <summary>
    /// True if the user has toggled logging on, OR if any connected device reports
    /// it is actively logging to its SD card. Reading from device state ensures the
    /// toggle reflects reality even when SD-card logging was started in a prior
    /// session and the device kept logging across a desktop reconnect. Streaming-mode
    /// state is not tracked here because <c>IsStreaming</c> is not on the
    /// <see cref="IStreamingDevice"/> interface; the streaming path updates state
    /// synchronously through the setter via the toggle, so this getter only needs to
    /// supplement that with the SD-card signal.
    /// </summary>
    public bool IsLogging
    {
        get => _isLogging || AnyDeviceActivelyLogging();
        set
        {
            var preSessionWarningShown = false;
            if (value && _diskSpaceMonitor != null)
            {
                var check = _diskSpaceMonitor.CheckPreLoggingSpace();
                if (check.Level == DiskSpaceLevel.Critical)
                {
                    // Notify bindings so TwoWay toggle reverts to false
                    OnPropertyChanged(nameof(IsLogging));
                    _ = ShowDiskSpaceMessage(
                        "Cannot Start Logging",
                        $"Only {check.AvailableMegabytes} MB of disk space remaining. " +
                        "Logging cannot start because the disk is critically low.\n\n" +
                        "Please free disk space by deleting old logging sessions or removing other files.");
                    return;
                }

                if (check.Level == DiskSpaceLevel.PreSessionWarning || check.Level == DiskSpaceLevel.Warning)
                {
                    preSessionWarningShown = true;
                    _ = ShowDiskSpaceMessage(
                        "Low Disk Space Warning",
                        $"Only {check.AvailableMegabytes} MB of disk space remaining. " +
                        "Logging may be stopped automatically if space runs out.\n\n" +
                        "Consider freeing disk space by deleting old logging sessions or removing other files.");
                }
            }

            _isLogging = value;
            LoggingManager.Instance.Active = value;
            if (_isLogging)
            {
                _diskSpaceMonitor?.StartMonitoring(suppressInitialWarning: preSessionWarningShown);

                foreach (var device in ConnectedDevices)
                {
                    if (device.Mode == DeviceMode.StreamToApp)
                    {
                        device.InitializeStreaming();
                    }
                    else if (device.Mode == DeviceMode.LogToDevice)
                    {
                        device.StartSdCardLogging();
                    }
                }
            }
            else
            {
                _diskSpaceMonitor?.StopMonitoring();

                foreach (var device in ConnectedDevices)
                {
                    if (device.Mode == DeviceMode.StreamToApp)
                    {
                        device.StopStreaming();
                    }
                    else if (device.Mode == DeviceMode.LogToDevice)
                    {
                        device.StopSdCardLogging();
                    }
                }
            }

            OnPropertyChanged(nameof(IsLogging));
            OnPropertyChanged(nameof(IsSdCardLoggingActive));
        }
    }

    /// <summary>
    /// True when at least one connected device reports it is actively logging to its SD card.
    /// Used by the Live Graph to show a "Logging to Device" status panel in place of the
    /// (necessarily empty) plot, since SD-mode samples never reach the desktop.
    /// </summary>
    public bool IsSdCardLoggingActive => ConnectedDevices.Any(d => d.IsLoggingToSdCard);

    private bool AnyDeviceActivelyLogging()
        => ConnectedDevices.Any(d => d.IsLoggingToSdCard);

    public bool CanToggleLogging
    {
        get => _canToggleLogging;
        private set
        {
            _canToggleLogging = value;
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    private int _notificationCount;

    [ObservableProperty]
    private string _versionName;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            _selectedIndex = value;
            CloseFlyouts();
            // Cancel any pending confirm overlay so its awaiter (e.g. an in-flight
            // delete) doesn't get stranded when the user navigates away from the
            // pane that owns the overlay.
            CompleteConfirm(false);
            OnPropertyChanged();
        }
    }

    public int SelectedStreamingFrequency
    {
        get => _selectedStreamingFrequency;
        set
        {
            if (value < 1) { return; }

            // Use IsLogging (not LoggingManager.Active) so the guard also blocks
            // changes when a connected device is reporting SD-card logging without
            // the local toggle ever having been flipped — e.g. on reconnect to a
            // device that's still logging from a previous desktop session.
            if (IsLogging)
            {
                var errorDialogViewModel = new ErrorDialogViewModel("Cannot change sampling frequency while logging.");
                _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
                return;
            }

            SelectedDevice.StreamingFrequency = value;
            _selectedStreamingFrequency = SelectedDevice.StreamingFrequency;
            OnPropertyChanged();
        }
    }

    public double FlyoutWidth
    {
        get
        {
            if (ViewWindowState == WindowState.Maximized)
            {
                return SystemParameters.WorkArea.Width - SidePanelWidth;
            }
            return Width - SidePanelWidth;
        }
    }

    public double FlyoutHeight
    {
        get
        {
            if (ViewWindowState == WindowState.Maximized)
            {
                return SystemParameters.WorkArea.Height - TopToolbarHeight;
            }
            return Height - TopToolbarHeight;
        }
    }

    public WindowState ViewWindowState
    {
        get => _viewWindowState;
        set
        {
            _viewWindowState = value;
            OnPropertyChanged("FlyoutWidth");
            OnPropertyChanged("FlyoutHeight");
        }
    }
    [ObservableProperty]
    private string _loggedSessionName;

    public string SelectedLoggingMode
    {
        get => _selectedLoggingMode;
        set
        {
            if (_selectedLoggingMode != value)
            {
                // Handle ComboBoxItem content
                var mode = value;
                if (value?.Contains("ComboBoxItem") == true)
                {
                    mode = value.Split(':').Last().Trim();
                }

                var isLogToDeviceMode = mode == "Log to Device";
                var deviceMode = isLogToDeviceMode ? DeviceMode.LogToDevice : DeviceMode.StreamToApp;
                var originalDeviceModes = ConnectedDevices.ToDictionary(device => device, device => device.Mode);

                try
                {
                    foreach (var device in ConnectedDevices)
                    {
                        device.SwitchMode(deviceMode);
                    }
                }
                catch (Exception ex)
                {
                    foreach (var originalDeviceMode in originalDeviceModes)
                    {
                        if (originalDeviceMode.Key.Mode == originalDeviceMode.Value)
                        {
                            continue;
                        }

                        try
                        {
                            originalDeviceMode.Key.SwitchMode(originalDeviceMode.Value);
                        }
                        catch (Exception rollbackException)
                        {
                            _appLogger.Warning(
                                $"Failed to roll back logging mode for {originalDeviceMode.Key.Name}: {rollbackException.Message}");
                        }
                    }

                    _appLogger.Error(ex, "Failed to switch device logging mode.");
                    throw;
                }

                _selectedLoggingMode = value;
                IsLogToDeviceMode = isLogToDeviceMode;
                LoggingManager.Instance.CurrentMode = isLogToDeviceMode ? LoggingMode.SdCard : LoggingMode.Stream;

                OnPropertyChanged();
            }
        }
    }

    public bool IsLogToDeviceMode
    {
        get => _isLogToDeviceMode;
        private set
        {
            if (_isLogToDeviceMode != value)
            {
                _isLogToDeviceMode = value;
                OnPropertyChanged();
            }
        }
    }

    public SdCardLogFormat SelectedSdCardLogFormat
    {
        get => _selectedSdCardLogFormat;
        set
        {
            if (_selectedSdCardLogFormat != value)
            {
                _selectedSdCardLogFormat = value;
                foreach (var device in ConnectedDevices.ToList())
                {
                    device.SdCardLogFormat = value;
                }
                OnPropertyChanged();
            }
        }
    }

    public DeviceLogsViewModel DeviceLogsViewModel { get; private set; }

    // Re-add properties for manually instantiated commands
    public ICommand DeleteLoggingSessionCommand { get; private set; }
    public AsyncRelayCommand DeleteAllLoggingSessionCommand { get; private set; }
    public ICommand ToggleChannelVisibilityCommand { get; private set; }
    public ICommand ToggleLoggedSeriesVisibilityCommand { get; private set; }
    /// <summary>
    /// Resolves the confirm overlay's awaitable Task with <c>true</c>.
    /// Bound to the affirmative button.
    /// </summary>
    public IRelayCommand ConfirmAffirmativeCommand { get; }

    /// <summary>
    /// Resolves the confirm overlay's awaitable Task with <c>false</c>.
    /// Bound to the cancel button and the scrim.
    /// </summary>
    public IRelayCommand ConfirmNegativeCommand { get; }
    #endregion

    #region Constructor
    /// <summary>
    /// Initializes a new instance of the view model using services from the desktop application container.
    /// </summary>
    public DaqifiViewModel() : this(
        ServiceLocator.Resolve<IDialogService>(),
        App.ServiceProvider?.GetService<IFirmwareUpdateService>(),
        App.ServiceProvider?.GetService<IFirmwareDownloadService>(),
        App.ServiceProvider?.GetService<ILogger<FirmwareUpdateService>>(),
        loggingContextFactory: App.ServiceProvider?.GetService<IDbContextFactory<LoggingContext>>())
    {
    }

    /// <summary>
    /// Initializes a new instance of the main desktop view model.
    /// </summary>
    /// <param name="dialogService">Dialog service used for modal and flyout dialogs.</param>
    /// <param name="firmwareUpdateService">Firmware update service for PIC32 updates.</param>
    /// <param name="firmwareDownloadService">Firmware download service for package acquisition.</param>
    /// <param name="firmwareLogger">Logger used when creating the default firmware update service.</param>
    /// <param name="wifiFirmwareUpdateServiceFactory">
    /// Factory used to create the WiFi firmware update service for a specific firmware version and COM port.
    /// </param>
    public DaqifiViewModel(
        IDialogService dialogService,
        IFirmwareUpdateService? firmwareUpdateService = null,
        IFirmwareDownloadService? firmwareDownloadService = null,
        ILogger<FirmwareUpdateService>? firmwareLogger = null,
        Func<string, string, IFirmwareUpdateService>? wifiFirmwareUpdateServiceFactory = null,
        IDbContextFactory<LoggingContext>? loggingContextFactory = null)
    {
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _firmwareDownloadService = firmwareDownloadService ?? CreateDefaultFirmwareDownloadService();
        var resolvedLogger = firmwareLogger ?? NullLogger<FirmwareUpdateService>.Instance;
        _firmwareUpdateService = firmwareUpdateService ?? CreateDefaultFirmwareUpdateService(_firmwareDownloadService, resolvedLogger);
        _loggingContextFactory = loggingContextFactory;
        _wifiFirmwareUpdateServiceFactory = wifiFirmwareUpdateServiceFactory ?? CreateWifiFirmwareUpdateService;

        ConfirmAffirmativeCommand = new RelayCommand(() => CompleteConfirm(true));
        ConfirmNegativeCommand = new RelayCommand(() => CompleteConfirm(false));

        // Track device-side logging/streaming state so the toggle and the Live Graph
        // overlay reflect what's *actually* happening, not just what the user clicked.
        // This catches cases like reconnecting to a device that's still SD-logging from
        // a previous desktop session, where _isLogging would otherwise stay false.
        ConnectedDevices.CollectionChanged += OnConnectedDevicesCollectionChanged;

        var app = Application.Current as App;
        if (app != null)
        {
            if (app.IsWindowInit)
            {
                try
                {
                    RegisterCommands();

                    // Manage connected streamingDevice list
                    ConnectionManager.Instance.PropertyChanged += UpdateUi;

                    // Manage data for plotting
                    LoggingManager.Instance.PropertyChanged += UpdateUi;
                    AttachLoggingSessionsCollection(LoggingManager.Instance.LoggingSessions);
                    Plotter = new PlotLogger();
                    LoggingManager.Instance.AddLogger(Plotter);

                    // Database logging
                    DbLogger = new DatabaseLogger(GetLoggingContextFactory());
                    LoggingManager.Instance.AddLogger(DbLogger);

                    // Device Logs View Model
                    DeviceLogsViewModel = new DeviceLogsViewModel();

                    //Xml profiles load
                    LoggingManager.Instance.AddAndRemoveProfileXml(null, false);
                    _ = new ObservableCollection<Profile>(LoggingManager.Instance.LoadProfilesFromXml());

                    // Notifications
                    _versionNotification = new VersionNotification();
                    _ = LoggingManager.Instance.CheckApplicationVersion(_versionNotification);

                    // Summary Logger
                    SummaryLogger = new SummaryLogger();
                    LoggingManager.Instance.AddLogger(SummaryLogger);

                    // Disk space monitoring
                    _diskSpaceMonitor = new DiskSpaceMonitor(App.DaqifiDataDirectory);
                    _diskSpaceMonitor.LowSpaceWarning += OnDiskSpaceLowWarning;
                    _diskSpaceMonitor.CriticalSpaceReached += OnDiskSpaceCritical;

                    if (LoggingManager.Instance.LoggingSessions.Count == 0)
                    {
                        LoggingManager.Instance.ReloadPersistedLoggingSessions();
                    }

                    // Configure default grid lines
                    Plotter.ShowingMinorXAxisGrid = false;
                    Plotter.ShowingMinorYAxisGrid = false;

                    FirewallConfiguration.InitializeFirewallRules();
                }
                catch (Exception ex)
                {
                    _appLogger.Error(ex, "DaqifiViewModel");
                }
            }

            app.IsWindowInit = true;
        }
    }

    private static LoggingManager? TryGetLoggingManager()
    {
        return App.ServiceProvider?.GetService<IDbContextFactory<LoggingContext>>() == null
            ? null
            : LoggingManager.Instance;
    }

    #endregion

    #region Register Command

    private void RegisterCommands()
    {
        DeleteLoggingSessionCommand = new AsyncRelayCommand<LoggingSession?>(DeleteLoggingSessionAsync);
        DeleteAllLoggingSessionCommand = new AsyncRelayCommand(DeleteAllLoggingSessionAsync, CanDeleteAllLoggingSession);
        ToggleChannelVisibilityCommand = new RelayCommand<IChannel>(ToggleChannelVisibility);
        ToggleLoggedSeriesVisibilityCommand = new RelayCommand<LoggedSeriesLegendItem>(ToggleLoggedSeriesVisibility);

        // Keep registration for external commands if necessary
        // HostCommands.ShutdownCommand.RegisterCommand(ShutdownCommand); // This would need adjustment if ShutdownCommand is generated
    }
    #endregion

    #region Command Logic
    private void ToggleChannelVisibility(IChannel? channel)
    {
        if (channel != null)
        {
            channel.IsVisible = !channel.IsVisible;
            // The PropertyChanged event on IsVisible should trigger UI updates in PlotLogger and the legend's DataTrigger.
            // No need to manually call UpdateUi here for ActiveInputChannels if it's already correctly populated.
        }
    }

    private void ToggleLoggedSeriesVisibility(LoggedSeriesLegendItem? item)
    {
        if (item != null)
        {
            item.IsVisible = !item.IsVisible;
            // The IsVisible setter in LoggedSeriesLegendItem handles updating
            // the ActualSeries.IsVisible and invalidating the plot.
        }
    }
    #endregion

    #region Command Callback Methods

    #region Updload firmware and update processes

    [RelayCommand]
    private async Task UploadFirmware()
    {
        if (IsFirmwareUploading)
        {
            return;
        }

        if (SelectedDevice?.ConnectionType != Device.ConnectionType.Usb)
        {
            return;
        }

        if (SelectedDevice is not SerialStreamingDevice serialStreamingDevice)
        {
            return;
        }

        SelectedDeviceSupportsFirmwareUpdate = true;
        HasErrorOccured = false;
        IsUploadComplete = false;
        UploadFirmwareProgress = 0;
        UploadWiFiProgress = 0;
        FirmwareUpdateStatusText = "Preparing firmware update...";

        _deviceBeingUpdated = SelectedDevice;
        ConnectionManager.Instance.DeviceBeingUpdated = _deviceBeingUpdated;

        var isManualUpload = !string.IsNullOrWhiteSpace(FirmwareFilePath);

        _firmwareUploadCts?.Dispose();
        _firmwareUploadCts = new CancellationTokenSource();
        IsFirmwareUploading = true;
        _appLogger.AddBreadcrumb("firmware", $"Firmware update started for {serialStreamingDevice.Name}");

        try
        {
            var coreDevice = serialStreamingDevice.ConnectedCoreStreamingDevice;

            if (!coreDevice.IsConnected)
            {
                _appLogger.Error($"Device {serialStreamingDevice.Name} is not connected. Cannot update firmware on a disconnected device.");
                NotificationList.Add(new Notifications
                {
                    Message = $"Please connect device {serialStreamingDevice.Name} before attempting firmware update.",
                    DeviceSerialNo = serialStreamingDevice.DeviceSerialNo
                });

                return;
            }

            if (!isManualUpload)
            {
                FirmwareUpdateStatusText = "Downloading latest firmware package...";
                FirmwareFilePath = await _firmwareDownloadService.DownloadLatestFirmwareAsync(
                    GetFirmwareDownloadDirectory(),
                    includePreRelease: true,
                    cancellationToken: _firmwareUploadCts.Token);
            }

            if (string.IsNullOrWhiteSpace(FirmwareFilePath) || !File.Exists(FirmwareFilePath))
            {
                throw new FileNotFoundException("Firmware file path is invalid or does not exist.", FirmwareFilePath);
            }

            var pic32Progress = new Progress<FirmwareUpdateProgress>(report =>
            {
                UploadFirmwareProgress = Math.Clamp((int)Math.Round(report.PercentComplete), 0, 100);
                if (!string.IsNullOrWhiteSpace(report.CurrentOperation))
                {
                    FirmwareUpdateStatusText = report.CurrentOperation;
                }
            });

            await _firmwareUpdateService.UpdateFirmwareAsync(
                coreDevice,
                FirmwareFilePath,
                pic32Progress,
                _firmwareUploadCts.Token);

            if (!isManualUpload)
            {
                await UpdateWifiModuleAsync(coreDevice, serialStreamingDevice, _firmwareUploadCts.Token);
            }

            IsUploadComplete = true;
            _appLogger.AddBreadcrumb("firmware", "Firmware update completed");
            ShowUploadSuccessMessage();
        }
        catch (OperationCanceledException)
        {
            FirmwareUpdateStatusText = "Firmware update canceled.";
            _appLogger.Warning("Firmware update canceled by user.");
            _appLogger.AddBreadcrumb("firmware", "Firmware update cancelled", Common.Loggers.BreadcrumbLevel.Warning);
        }
        catch (FirmwareUpdateException ex)
        {
            _appLogger.AddBreadcrumb("firmware", $"Firmware update failed: {ex.FailedState}", Common.Loggers.BreadcrumbLevel.Error);
            HandleFirmwareUpdateException(ex);
        }
        catch (Exception ex)
        {
            HasErrorOccured = true;
            _appLogger.Error(ex, "Problem Uploading Firmware");
            _appLogger.AddBreadcrumb("firmware", "Firmware update failed", Common.Loggers.BreadcrumbLevel.Error);
            ShowFirmwareErrorDialog("Firmware update failed. Please try again.");
        }
        finally
        {
            IsFirmwareUploading = false;
            _firmwareUploadCts?.Dispose();
            _firmwareUploadCts = null;
            _deviceBeingUpdated = null;
            ConnectionManager.Instance.DeviceBeingUpdated = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelFirmwareUpload))]
    private void CancelFirmwareUpload()
    {
        if (!IsFirmwareUploading)
        {
            return;
        }

        FirmwareUpdateStatusText = "Canceling firmware update...";
        _firmwareUploadCts?.Cancel();
    }

    private bool CanCancelFirmwareUpload()
    {
        return IsFirmwareUploading;
    }

    private async Task UpdateWifiModuleAsync(
        Daqifi.Core.Device.IStreamingDevice coreDevice,
        SerialStreamingDevice serialStreamingDevice,
        CancellationToken cancellationToken)
    {
        // Core's WiFi updater also performs its own version probe when the passed device
        // implements ILanChipInfoProvider. Keep the explicit desktop-side check here so
        // desktop can skip unnecessary downloads and surface the current/update version in UI.
        if (serialStreamingDevice is ILanChipInfoProvider lanChipProvider)
        {
            FirmwareUpdateStatusText = "Checking WiFi firmware version...";
            _appLogger.Information("Checking WiFi firmware version before deciding whether to flash the WiFi module.");

            var chipInfo = await TryGetLanChipInfoAsync(lanChipProvider, cancellationToken);

            if (chipInfo == null)
            {
                _appLogger.Warning("WiFi chip info unavailable after startup retries; continuing with WiFi update.");
                FirmwareUpdateStatusText = "WiFi firmware version unavailable; continuing with update.";
            }
            else
            {
                _appLogger.Information(
                    $"WiFi chip info query succeeded. Device WiFi firmware version: {chipInfo.FwVersion}.");

                var latestRelease = await _firmwareDownloadService.GetLatestWifiReleaseAsync(cancellationToken);

                if (latestRelease != null)
                {
                    var latestVersion = NormalizeWifiFirmwareVersion(latestRelease.TagName);
                    if (IsWifiVersionCurrent(chipInfo.FwVersion, latestVersion))
                    {
                        FirmwareUpdateStatusText = $"WiFi firmware already up to date ({chipInfo.FwVersion}).";
                        _appLogger.Information(
                            $"WiFi firmware is already up to date (device: {chipInfo.FwVersion}, latest: {latestVersion}); skipping WiFi flash.");
                        UploadWiFiProgress = 100;
                        return;
                    }

                    FirmwareUpdateStatusText = $"WiFi update available ({chipInfo.FwVersion} → {latestVersion}). Downloading...";
                    _appLogger.Information(
                        $"WiFi firmware update required (device: {chipInfo.FwVersion}, latest: {latestVersion}); proceeding with WiFi flash.");
                }
                else
                {
                    _appLogger.Warning("Latest WiFi firmware release metadata was unavailable; continuing with WiFi update.");
                }
            }
        }

        FirmwareUpdateStatusText = "Downloading WiFi firmware package...";
        var wifiDownloadProgress = new Progress<int>(percent =>
        {
            // Map download progress into the initial segment of the WiFi bar.
            UploadWiFiProgress = Math.Clamp((int)Math.Round(percent * 0.2), 0, 20);
        });

        var wifiPackage = await _firmwareDownloadService.DownloadWifiFirmwareAsync(
            GetWifiDownloadDirectory(),
            wifiDownloadProgress,
            cancellationToken);

        if (wifiPackage == null)
        {
            throw new InvalidOperationException("No WiFi firmware package was found for update.");
        }

        var wifiVersion = NormalizeWifiFirmwareVersion(wifiPackage.Value.Version);
        FirmwareUpdateStatusText = $"Updating WiFi module ({wifiVersion})...";
        var wifiUpdateProgress = new Progress<FirmwareUpdateProgress>(report =>
        {
            UploadWiFiProgress = Math.Clamp((int)Math.Round(report.PercentComplete), 0, 100);
            if (!string.IsNullOrWhiteSpace(report.CurrentOperation))
            {
                FirmwareUpdateStatusText = report.CurrentOperation;
            }
        });

        // Preserve the legacy serial prep/reset sequence now that the firmware flow uses the
        // underlying Core device directly instead of routing through a desktop-shaped adapter.
        var lanUpdateModeEnabled = false;
        try
        {
            lanUpdateModeEnabled = serialStreamingDevice.EnableLanUpdateMode();

            var wifiUpdateService = _wifiFirmwareUpdateServiceFactory(wifiVersion, serialStreamingDevice.PortName);
            try
            {
                await wifiUpdateService.UpdateWifiModuleAsync(
                    coreDevice,
                    wifiPackage.Value.ExtractedPath,
                    wifiUpdateProgress,
                    cancellationToken);
            }
            finally
            {
                (wifiUpdateService as IDisposable)?.Dispose();
            }
        }
        finally
        {
            if (lanUpdateModeEnabled)
            {
                serialStreamingDevice.ResetLanAfterUpdate();
            }
        }
    }

    private async Task<LanChipInfo?> TryGetLanChipInfoAsync(
        ILanChipInfoProvider lanChipProvider,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= WifiChipInfoMaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var chipInfo = await lanChipProvider.GetLanChipInfoAsync(cancellationToken);
                if (chipInfo != null)
                {
                    return chipInfo;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _appLogger.Warning(
                    $"WiFi chip info query attempt {attempt}/{WifiChipInfoMaxAttempts} failed: {ex.Message}");
            }

            if (attempt >= WifiChipInfoMaxAttempts)
            {
                break;
            }

            _appLogger.Information(
                $"WiFi chip info unavailable on attempt {attempt}/{WifiChipInfoMaxAttempts}; retrying after startup delay.");
            FirmwareUpdateStatusText = "Waiting for device to finish starting up before checking WiFi firmware version...";
            await Task.Delay(WifiChipInfoRetryDelay, cancellationToken);
        }

        return null;
    }

    private FirmwareUpdateService CreateWifiFirmwareUpdateService(string wifiVersion, string portName)
    {
        var firmwareLogger = App.ServiceProvider?.GetService<ILogger<FirmwareUpdateService>>()
            ?? NullLogger<FirmwareUpdateService>.Instance;

        // Bridge activation action: opened at the "Power cycle WINC" prompt to trigger the
        // device's bridge-mode state machine right before the flash tool starts programming.
        // By deferring APPLY (SYSTem:COMMunicate:LAN:APPLY) until this point we give the
        // firmware a guaranteed promptResponseDelay window (2 s) to complete the WiFi
        // deinit/reinit cycle and call wifi_serial_bridge_Init() before the flash tool
        // issues its first serial bridge query.
        Action bridgeActivationAction = () =>
        {
            _appLogger.Information($"Opening {portName} to send bridge activation commands.");
            try
            {
                // USB CDC virtual ports ignore the baud rate; match the Core transport defaults.
                using var port = new SerialPort(portName, 9600, Parity.None, 8, StopBits.One)
                {
                    DtrEnable = true,
                    RtsEnable = false,
                    WriteTimeout = 2000
                };
                port.Open();
                // Brief pause to allow the DTR signal to be recognised by the firmware
                // before we send commands (firmware checks isCdcHostConnected via DTR).
                Thread.Sleep(200);
                // Re-assert the FW-update-requested flag (idempotent; belt-and-suspenders).
                port.Write("SYSTem:COMMUnicate:LAN:FWUpdate\n");
                Thread.Sleep(100);
                // Trigger the WiFi manager REINIT → bridge-mode state machine.
                port.Write("SYSTem:COMMunicate:LAN:APPLY\n");
                // Give the firmware a moment to enqueue the APPLY before we close the port.
                Thread.Sleep(300);
                _appLogger.Information("Bridge activation commands sent successfully.");
            }
            catch (Exception ex)
            {
                _appLogger.Warning($"Bridge activation failed for {portName}: {ex.Message}");
            }
        };

        return new FirmwareUpdateService(
            new HidLibraryTransport(),
            _firmwareDownloadService,
            new WifiPromptDelayProcessRunner(
                new ProcessExternalProcessRunner(),
                promptResponseDelay: TimeSpan.FromSeconds(2),
                bridgeActivationAction: bridgeActivationAction),
            firmwareLogger,
            options: new FirmwareUpdateServiceOptions
            {
                // winc_flash_tool.cmd requires an explicit release version folder.
                // Keep legacy argument profile used by shipped WINC tool bundle.
                WifiFlashToolArgumentsTemplate = $"/p {{port}} /d WINC1500 /v {wifiVersion} /k /e /i aio /w",
                WifiPortOverride = portName,
                // After sending FWUpdate (flag-only, no APPLY), disconnect quickly so the
                // COM port is free for the bridge activation raw write at the "Power cycle
                // WINC" prompt.  The FWUpdate flag persists in firmware RAM until APPLY fires.
                PostLanFirmwareModeDelay = TimeSpan.FromMilliseconds(100),
                // Give Windows a little more time to re-enumerate the UART before reconnect attempts.
                PostWifiReconnectDelay = TimeSpan.FromSeconds(3)
            });
    }

    private static string NormalizeWifiFirmwareVersion(string rawVersion)
    {
        var normalized = (rawVersion ?? string.Empty).Trim();
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[1..];
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("WiFi firmware version metadata is missing.");
        }

        // Keep command argument safe even if an unexpected tag format appears.
        var invalidChars = new[] { ' ', '\t', '\r', '\n', '"', '\'', ';' };
        if (normalized.IndexOfAny(invalidChars) >= 0)
        {
            throw new InvalidOperationException($"Invalid WiFi firmware version tag '{rawVersion}'.");
        }

        return normalized;
    }

    private static bool IsWifiVersionCurrent(string deviceVersion, string latestVersion)
    {
        if (!FirmwareVersion.TryParse(deviceVersion, out var device)) return false;
        if (!FirmwareVersion.TryParse(latestVersion, out var latest)) return false;
        return device >= latest;
    }

    private void HandleFirmwareUpdateException(FirmwareUpdateException exception)
    {
        HasErrorOccured = true;

        var summary = $"Firmware update failed during '{exception.Operation}' ({exception.FailedState}).";
        _appLogger.Error(exception, summary);

        if (!string.IsNullOrWhiteSpace(exception.RecoveryGuidance))
        {
            _appLogger.Warning($"Firmware recovery guidance: {exception.RecoveryGuidance}");
        }

        var dialogMessage = summary;
        if (!string.IsNullOrWhiteSpace(exception.RecoveryGuidance))
        {
            dialogMessage += $"{Environment.NewLine}{Environment.NewLine}Suggested recovery: {exception.RecoveryGuidance}";
        }

        ShowFirmwareErrorDialog(dialogMessage);
    }

    private void ShowFirmwareErrorDialog(string message)
    {
        void ShowDialog()
        {
            var errorDialogViewModel = new ErrorDialogViewModel(message);
            _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            ShowDialog();
            return;
        }

        dispatcher.Invoke(ShowDialog);
    }

    private static string GetFirmwareDownloadDirectory()
    {
        var firmwareDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "DAQiFi",
            "Firmware",
            "PIC32");
        Directory.CreateDirectory(firmwareDirectory);
        return firmwareDirectory;
    }

    private static string GetWifiDownloadDirectory()
    {
        var wifiDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "DAQiFi",
            "Firmware",
            "WiFi");
        Directory.CreateDirectory(wifiDirectory);
        return wifiDirectory;
    }

    private static IFirmwareDownloadService CreateDefaultFirmwareDownloadService()
    {
        return new GitHubFirmwareDownloadService(new HttpClient());
    }

    private static IFirmwareUpdateService CreateDefaultFirmwareUpdateService(
        IFirmwareDownloadService firmwareDownloadService,
        ILogger<FirmwareUpdateService> logger)
    {
        return new FirmwareUpdateService(
            new HidLibraryTransport(),
            firmwareDownloadService,
            new ProcessExternalProcessRunner(),
            logger);
    }

    #endregion
    [RelayCommand]
    private void ShowConnectionDialog()
    {
        _connectionDialogViewModel = new ConnectionDialogViewModel();
        _connectionDialogViewModel.StartConnectionFinders();
        _dialogService.ShowDialog<ConnectionDialog>(this, _connectionDialogViewModel);
    }

    [RelayCommand]
    private void CloseAppSettings() => IsAppSettingsOpen = false;

    [RelayCommand]
    private void RemoveChannel(IChannel channelToRemove)
    {
        var device = ConnectionManager.Instance.ConnectedDevices.FirstOrDefault(x => x.DeviceSerialNo == channelToRemove.DeviceSerialNo);
        var channel = device.DataChannels.FirstOrDefault(x => x.DeviceSerialNo == channelToRemove.DeviceSerialNo && x.Name == channelToRemove.Name);
        if (device != null && channel != null)
        {
            LoggingManager.Instance.Unsubscribe(channel);
            device.RemoveChannel(channel);
        }
    }

    [RelayCommand]
    private void DisconnectDevice(IStreamingDevice? deviceToDisconnect)
    {
        if (deviceToDisconnect == null)
        {
            return;
        }

        foreach (var channel in deviceToDisconnect.DataChannels)
        {
            if (channel.IsActive)
            {
                LoggingManager.Instance.Unsubscribe(channel);
            }
        }
        ConnectionManager.Instance.Disconnect(deviceToDisconnect);
        RemoveNotification(deviceToDisconnect);

        if (deviceToDisconnect.Equals(SelectedDevice))
        {
            SelectedDevice = null;
        }
    }

    [RelayCommand]
    public void Shutdown()
    {
        foreach (var device in ConnectedDevices)
        {
            device.Disconnect();
        }
    }

    [RelayCommand]
    public async Task UpdateNetworkConfiguration()
    {
        ResetNetworkSettingsStatus();

        // Guard the happy-path below: a device can disappear while the drawer
        // is open (disconnect, tab switch, etc.), and the underlying
        // UpdateNetworkConfiguration() throws when the connection is gone.
        var device = SelectedDevice;
        if (device == null)
        {
            NetworkSettingsError = "Select a device before applying WiFi settings.";
            return;
        }
        if (!device.IsConnected)
        {
            NetworkSettingsError = "Cannot apply WiFi settings — the device is not connected.";
            return;
        }

        try
        {
            await device.UpdateNetworkConfiguration();
            _ = ShowNetworkSettingsAppliedStatusAsync();
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Failed to update network configuration");
            NetworkSettingsError = "Failed to apply WiFi settings. See the application log for details.";
        }
    }

    private void ResetNetworkSettingsStatus()
    {
        CancelAndDisposeNetworkSettingsCts();
        NetworkSettingsApplied = false;
        NetworkSettingsError = null;
    }

    private async Task ShowNetworkSettingsAppliedStatusAsync()
    {
        CancelAndDisposeNetworkSettingsCts();
        _networkSettingsAppliedCts = new CancellationTokenSource();
        var token = _networkSettingsAppliedCts.Token;

        NetworkSettingsApplied = true;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), token);
            NetworkSettingsApplied = false;
        }
        catch (TaskCanceledException) { }
    }

    private void CancelAndDisposeNetworkSettingsCts()
    {
        var cts = _networkSettingsAppliedCts;
        if (cts == null)
        {
            return;
        }
        _networkSettingsAppliedCts = null;
        try
        {
            cts.Cancel();
        }
        finally
        {
            cts.Dispose();
        }
    }

    [RelayCommand]
    public void BrowseForFirmware()
    {
        using var openFileDialog = new OpenFileDialog
        {
            Filter = "Firmware Files (*.hex)|*.hex"
        };
        if (openFileDialog.ShowDialog() == DialogResult.OK)
        {
            FirmwareFilePath = openFileDialog.FileName;
        }
    }

    [RelayCommand]
    private void OpenLiveGraphSettings()
    {
        CloseFlyouts();
        IsLiveGraphSettingsOpen = true;
    }

    [RelayCommand]
    private void OpenFirmwareUpdateSettings(IStreamingDevice? device)
    {
        if (device == null)
        {
            return;
        }

        SelectedDeviceSupportsFirmwareUpdate = device.ConnectionType == Device.ConnectionType.Usb;

        CloseFlyouts();
        SelectedDevice = device;
        IsFirmwareUpdatationFlyoutOpen = true;
    }

    [RelayCommand]
    private void OpenLogSummary()
    {
        CloseFlyouts();
        IsLogSummaryOpen = true;
    }

    [RelayCommand]
    private void CloseLoggedSessionSettings()
    {
        IsLoggingSessionSettingsOpen = false;
    }

    [RelayCommand]
    private void OpenLoggingSessionSettings(LoggingSession? session)
    {
        if (session == null)
        {
            return;
        }

        CloseFlyouts();
        SelectedLoggingSession = session;
        if (session.Name.Length == 0)
        {
            LoggedSessionName = "Session_" + session.ID;
        }
        else
        {
            LoggedSessionName = session.Name;
        }
        LoggedSessionName = session.Name;
        IsLoggingSessionSettingsOpen = true;
    }

    [RelayCommand]
    private void DisplayLoggingSession(LoggingSession? session)
    {
        if (session == null)
        {
            DbLogger.ClearPlot();
            return;
        }

        SelectedLoggingSession = session;
        IsLoggedDataBusy = true;
        LoggedDataBusyReason = "Loading " + SelectedLoggingSession.Name;
        var bw = new BackgroundWorker();
        bw.DoWork += delegate
        {
            try
            {
                DbLogger.DisplayLoggingSession(SelectedLoggingSession);
            }
            catch (Exception ex)
            {
                _appLogger.Error(ex, $"Failed to display logging session {SelectedLoggingSession.ID}.");
            }
        };

        bw.RunWorkerCompleted += (s, e) =>
        {
            IsLoggedDataBusy = false;
            LoggedDataBusyReason = string.Empty;
        };

        bw.RunWorkerAsync();
    }

    [RelayCommand]
    private void ExportLoggingSession(LoggingSession? session)
    {
        if (session == null)
        {
            _appLogger.Error("Error exporting logging session");
            return;
        }

        SelectedLoggingSession = session;
        var exportDialogViewModel = new ExportDialogViewModel(SelectedLoggingSession.ID);
        _dialogService.ShowDialog<ExportDialog>(this, exportDialogViewModel);
    }

    [RelayCommand(CanExecute = nameof(CanExportAllLoggingSession))]
    private void ExportAllLoggingSession()
    {
        if (LoggingSessions.Count == 0)
        {
            _appLogger.Error("Error exporting all logging sessions");
            return;
        }

        var exportDialogViewModel = new ExportDialogViewModel(LoggingSessions);
        _dialogService.ShowDialog<ExportDialog>(this, exportDialogViewModel);
    }

    [RelayCommand]
    private async Task ImportSdCardLogFile()
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "SD Card Log Files (*.bin;*.json;*.csv)|*.bin;*.json;*.csv|Protobuf (*.bin)|*.bin|JSON (*.json)|*.json|CSV (*.csv)|*.csv|All Files (*.*)|*.*",
                Title = "Select SD Card Log File to Import"
            };

            if (dialog.ShowDialog() != true) return;

            IsLoggedDataBusy = true;
            LoggedDataBusyReason = $"Importing {System.IO.Path.GetFileName(dialog.FileName)}...";

            var loggingContext = App.ServiceProvider.GetRequiredService<IDbContextFactory<LoggingContext>>();
            var importer = new SdCardSessionImporter(loggingContext);

            var progress = new Progress<ImportProgress>(p =>
            {
                LoggedDataBusyReason = $"Importing... {p.SamplesProcessed:N0} samples";
            });

            var session = await Task.Run(() =>
                importer.ImportFromFileAsync(dialog.FileName, null, progress, CancellationToken.None));

            Application.Current.Dispatcher.Invoke(() =>
            {
                LoggingManager.Instance.LoggingSessions.Add(session);
            });

            await ShowMessage("Import Complete",
                $"Successfully imported {System.IO.Path.GetFileName(dialog.FileName)}",
                MessageDialogStyle.Affirmative);
        }
        catch (OperationCanceledException)
        {
            // User cancelled — do nothing
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Error importing SD card log file");
            await ShowMessage("Import Failed",
                "Failed to import the selected file. Please verify the file is a valid SD card log file and try again.",
                MessageDialogStyle.Affirmative);
        }
        finally
        {
            IsLoggedDataBusy = false;
            LoggedDataBusyReason = string.Empty;
        }
    }

    private async Task DeleteLoggingSessionAsync(LoggingSession? session)
    {
        try
        {
            if (session == null)
            {
                _appLogger.Error("Error deleting logging session: Invalid object provided.");
                return;
            }

            SelectedLoggingSession = session;

            var confirmed = await ShowConfirm(
                "Delete Confirmation",
                $"Are you sure you want to delete {SelectedLoggingSession.Name}?",
                affirmativeLabel: "DELETE",
                isDestructive: true);
            if (!confirmed)
            {
                return;
            }

            IsLoggedDataBusy = true;
            LoggedDataBusyReason = $"Deleting Logging Session #{SelectedLoggingSession.ID}";
            var bw = new BackgroundWorker();
            var sessionToDelete = SelectedLoggingSession;
            bw.DoWork += delegate
            {
                var deleteSucceeded = false;
                try
                {
                    DbLogger.DeleteLoggingSession(sessionToDelete);
                    deleteSucceeded = true;
                }
                catch (Exception dbEx)
                {
                    _appLogger.Error(dbEx, $"Failed to delete session {sessionToDelete.ID} from database.");
                }

                if (deleteSucceeded)
                {
                    Application.Current.Dispatcher.Invoke(delegate
                    {
                        LoggingManager.Instance.LoggingSessions.Remove(sessionToDelete);
                        NotifyLoggingSessionsChanged();
                    });
                }
            };

            bw.RunWorkerCompleted += (s, e) =>
            {
                IsLoggedDataBusy = false;
                LoggedDataBusyReason = string.Empty;
            };

            bw.RunWorkerAsync();
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Error initiating logging session deletion");
        }
    }

    private async Task DeleteAllLoggingSessionAsync()
    {
        try
        {
            if (LoggingSessions.Count == 0)
            {
                return;
            }

            if (LoggingManager.Instance.Active)
            {
                await ShowMessage(
                    "Cannot Delete",
                    "Please stop logging before deleting all sessions.",
                    MessageDialogStyle.Affirmative);
                return;
            }

            var confirmed = await ShowConfirm(
                "Delete Confirmation",
                "Are you sure you want to delete all logging sessions? This cannot be undone.",
                affirmativeLabel: "DELETE ALL",
                isDestructive: true);
            if (!confirmed)
            {
                return;
            }

            IsLoggedDataBusy = true;
            LoggedDataBusyReason = "Deleting All Logging Sessions";

            try
            {
                var contextFactory = GetLoggingContextFactory();
                await Task.Run(() => DeleteAllLoggingSessionsFromStorage(contextFactory));
                LoggingManager.Instance.LoggingSessions.Clear();
                NotifyLoggingSessionsChanged();
                DbLogger.ClearPlot();
            }
            catch (IOException ioEx)
            {
                _appLogger.Error(ioEx, "Database file is in use. Please try again.");
            }
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Error during deletion of all logging sessions");
        }
        finally
        {
            IsLoggedDataBusy = false;
            LoggedDataBusyReason = string.Empty;
        }
    }

    private void DeleteAllLoggingSessionsFromStorage(IDbContextFactory<LoggingContext> contextFactory)
    {
        DbLogger.SuspendConsumer();
        try
        {
            DbLogger.ClearBuffer();

            // Release all pooled SQLite connections so the file is not locked.
            SqliteConnection.ClearAllPools();

            var dbPath = App.DatabasePath;
            DeleteFileIfExists(dbPath);
            DeleteFileIfExists(dbPath + "-wal");
            DeleteFileIfExists(dbPath + "-shm");

            // Recreate the database schema. Constructing a context does not
            // create tables — only Migrate() (or EnsureCreated) does. Without
            // this, the next session-start query against Samples/Sessions
            // throws "no such table: Samples".
            using var context = contextFactory.CreateDbContext();
            context.Database.Migrate();
        }
        finally
        {
            DbLogger.ResumeConsumer();
        }
    }

    private IDbContextFactory<LoggingContext> GetLoggingContextFactory()
    {
        return _loggingContextFactory
            ?? App.ServiceProvider?.GetService<IDbContextFactory<LoggingContext>>()
            ?? throw new InvalidOperationException("Logging context factory is not available.");
    }

    private void AttachLoggingSessionsCollection(ObservableCollection<LoggingSession> loggingSessions)
    {
        if (ReferenceEquals(_observedLoggingSessions, loggingSessions))
        {
            return;
        }

        if (_observedLoggingSessions != null)
        {
            _observedLoggingSessions.CollectionChanged -= OnLoggingSessionsCollectionChanged;
        }

        _observedLoggingSessions = loggingSessions;
        _observedLoggingSessions.CollectionChanged += OnLoggingSessionsCollectionChanged;
    }

    private void OnLoggingSessionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            _ = dispatcher.InvokeAsync(NotifyLoggingSessionsChanged);
            return;
        }

        NotifyLoggingSessionsChanged();
    }

    private void NotifyLoggingSessionsChanged()
    {
        OnPropertyChanged(nameof(LoggingSessions));
        OnPropertyChanged(nameof(HasLoggingSessions));
        DeleteAllLoggingSessionCommand?.NotifyCanExecuteChanged();
        ExportAllLoggingSessionCommand.NotifyCanExecuteChanged();
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private bool CanDeleteAllLoggingSession()
    {
        return LoggingSessions.Count > 0;
    }

    [RelayCommand]
    private void RebootDevice(IStreamingDevice? deviceToReboot)
    {
        if (deviceToReboot == null)
        {
            return;
        }

        foreach (var channel in deviceToReboot.DataChannels)
        {
            if (channel.IsActive)
            {
                LoggingManager.Instance.Unsubscribe(channel);
            }
        }
        ConnectionManager.Instance.Reboot(deviceToReboot);

        if (deviceToReboot.Equals(SelectedDevice))
        {
            SelectedDevice = null;
        }
    }

    [RelayCommand]
    private void OpenHelp()
    {
        try
        {
            const string url = "https://www.daqifi.com/support";

            var processStartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };

            System.Diagnostics.Process.Start(processStartInfo);
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Error opening help URL");
        }
    }

    #region Firmware version checking methods

    private string _latestFirmwareVersion;
    public string LatestFirmwareVersionText => _latestFirmwareVersion;
    [RelayCommand]
    public async Task GetFirmwareupdatationList()
    {
        var connectedDevices = ConnectionManager.Instance.ConnectedDevices;
        if (connectedDevices.Count == 0)
        {
            return;
        }

        try
        {
            var latestRelease = await _firmwareDownloadService.GetLatestReleaseAsync(includePreRelease: true);
            _latestFirmwareVersion = latestRelease?.Version.ToString() ?? string.Empty;
            OnPropertyChanged(nameof(LatestFirmwareVersionText));

            if (latestRelease == null)
            {
                return;
            }

            foreach (var device in connectedDevices)
            {
                var updateCheck = await _firmwareDownloadService.CheckForUpdateAsync(
                    device.DeviceVersion ?? string.Empty,
                    includePreRelease: true);
                var isOutdated = updateCheck.UpdateAvailable;
                device.IsFirmwareOutdated = isOutdated;

                if (!string.IsNullOrWhiteSpace(device.DeviceSerialNo))
                {
                    if (isOutdated)
                    {
                        AddNotification(device, latestRelease.Version.ToString());
                    }
                    else
                    {
                        RemoveNotification(device);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _appLogger.Warning($"Failed to check firmware updates: {ex.Message}");
        }
    }
    private void RemoveNotification()
    {
        foreach (var notification in NotificationList.ToList())
        {
            var deviceIsDisconnected = !ConnectionManager.Instance.ConnectedDevices
                .Any(device => device.DeviceSerialNo == notification.DeviceSerialNo);

            if (deviceIsDisconnected)
            {
                NotificationList.Remove(notification);
            }
        }
        NotificationCount = NotificationList.Count;
    }

    private void AddNotification(IStreamingDevice device, string latestFirmware)
    {
        var message = $"Device With Serial {device.DeviceSerialNo} has Outdated Firmware. Please Update to Version {latestFirmware}.";

        var existingNotification = NotificationList.FirstOrDefault(n => n.DeviceSerialNo != null
                                                                        && n.IsFirmwareUpdate
                                                                        && n.DeviceSerialNo == device.DeviceSerialNo);

        if (existingNotification == null)
        {
            NotificationList.Add(new Notifications
            {
                DeviceSerialNo = device.DeviceSerialNo,
                Message = message,
                IsFirmwareUpdate = true
            });
        }

        NotificationCount = NotificationList.Count;
    }
    private void RemoveNotification(IStreamingDevice deviceToRemove)
    {
        if (deviceToRemove?.DeviceSerialNo == null)
        {
            return;
        }

        var notificationsToRemove = NotificationList
            .FirstOrDefault(x => x.DeviceSerialNo != null && x.DeviceSerialNo == deviceToRemove.DeviceSerialNo && x.IsFirmwareUpdate);

        if (notificationsToRemove != null)
        {
            NotificationList.Remove(notificationsToRemove);
            NotificationCount = NotificationList.Count;
        }
    }

    #endregion

    [RelayCommand]
    private void OpenNotifications()
    {
        IsNotificationsOpen = true;
    }

    private void ShowUploadSuccessMessage()
    {
        void ShowDialog()
        {
            var successDialogViewModel =
                new SuccessDialogViewModel("Firmware update completed successfully.");
            _dialogService.ShowDialog<SuccessDialog>(this, successDialogViewModel);
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            ShowDialog();
            CloseFlyouts();
            return;
        }

        dispatcher.Invoke(ShowDialog);
        CloseFlyouts();
    }

    #endregion

    #region Helper Methods

    private bool EnsureAnyDeviceConnected()
    {
        if (ConnectionManager.Instance.ConnectedDevices.Count > 0) return true;
        _dialogService.ShowDialog<ErrorDialog>(this, new ErrorDialogViewModel("Please connect a device before creating a profile."));
        return false;
    }

    #endregion

    #region Methods
    public Task UpdateConnectedDeviceUI()
    {
        UploadFirmwareProgress = 0;
        UploadWiFiProgress = 0;

        foreach (var connectedDevice in ConnectionManager.Instance.ConnectedDevices)
        {
            var SerailDeviceProperty = connectedDevice.GetType().GetProperty("DeviceVersion");
            var DeviceVersion = SerailDeviceProperty.GetValue(connectedDevice)?.ToString();
            if (!string.IsNullOrEmpty(_latestFirmwareVersion))
            {
                connectedDevice.IsFirmwareOutdated = VersionHelper.Compare(DeviceVersion, _latestFirmwareVersion) < 0;
            }

            ConnectedDevices.Add(connectedDevice);

            // Sync SD card log format so newly connected devices match the UI selection
            connectedDevice.SdCardLogFormat = _selectedSdCardLogFormat;

            // Subscribe to debug events if this is a streaming device
            if (connectedDevice is AbstractStreamingDevice streamingDevice)
            {
                streamingDevice.DebugDataReceived += OnDebugDataReceived;
                streamingDevice.SetDebugMode(IsDebugModeEnabled);
            }
        }

        return Task.CompletedTask;
    }

    private readonly HashSet<IStreamingDevice> _loggingStateSubscribedDevices = [];

    private void OnConnectedDevicesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // ObservableCollection.Clear() raises a Reset with OldItems == null, so we
        // can't rely on the args alone — diff against our tracked-subscription set
        // to handle Reset, Replace, and per-item adds/removes uniformly.
        var current = new HashSet<IStreamingDevice>(ConnectedDevices);

        foreach (var stale in _loggingStateSubscribedDevices.Except(current).ToList())
        {
            stale.PropertyChanged -= OnDeviceLoggingStateChanged;
            _loggingStateSubscribedDevices.Remove(stale);
        }

        foreach (var added in current.Except(_loggingStateSubscribedDevices).ToList())
        {
            added.PropertyChanged += OnDeviceLoggingStateChanged;
            _loggingStateSubscribedDevices.Add(added);
        }

        RaiseLoggingStateChanged();
    }

    private void OnDeviceLoggingStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IStreamingDevice.IsLoggingToSdCard))
        {
            RaiseLoggingStateChanged();
        }
    }

    private void RaiseLoggingStateChanged()
    {
        OnPropertyChanged(nameof(IsLogging));
        OnPropertyChanged(nameof(IsSdCardLoggingActive));
        UpdateSdLoggingTimer();
    }

    private void UpdateSdLoggingTimer()
    {
        var active = IsSdCardLoggingActive;
        if (active && _sdLoggingElapsedTimer == null)
        {
            _sdLoggingStartedAt = DateTime.UtcNow;
            SdLoggingElapsed = "00:00:00";
            _sdLoggingElapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _sdLoggingElapsedTimer.Tick += OnSdLoggingTimerTick;
            _sdLoggingElapsedTimer.Start();
        }
        else if (!active && _sdLoggingElapsedTimer != null)
        {
            _sdLoggingElapsedTimer.Stop();
            _sdLoggingElapsedTimer.Tick -= OnSdLoggingTimerTick;
            _sdLoggingElapsedTimer = null;
            _sdLoggingStartedAt = null;
        }
    }

    private void OnSdLoggingTimerTick(object? sender, EventArgs e)
    {
        if (_sdLoggingStartedAt is { } start)
        {
            var elapsed = DateTime.UtcNow - start;
            // TimeSpan's "hh" specifier is the Hours component (0-23), so it wraps
            // at 24h. SD-card logging sessions can run arbitrarily long; format off
            // TotalHours instead so multi-day sessions display correctly.
            var totalHours = (int)elapsed.TotalHours;
            SdLoggingElapsed = $"{totalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        }
    }

    public async void UpdateUi(object sender, PropertyChangedEventArgs args)
    {

        switch (args.PropertyName)
        {
            case "SubscribedProfiles":

                if (LoggingManager.Instance.SubscribedProfiles.Count == 0)
                { Profiles.Clear(); }
                foreach (var connectedProfiles in LoggingManager.Instance.SubscribedProfiles)
                {
                    if (Profiles.All(x => x.ProfileId != connectedProfiles.ProfileId))
                    {
                        Profiles.Add(connectedProfiles);
                    }
                }
                break;
            case "ConnectedDevices":
                ConnectedDevices.Clear();
                await UpdateConnectedDeviceUI();

                // Unsubscribe channels for devices that are no longer connected.
                // Covers auto-removal paths (physical unplug, WiFi timeout, serial removed)
                // that bypass DisconnectDevice and skip its per-channel Unsubscribe loop.
                var stillConnected = ConnectionManager.Instance.ConnectedDevices
                    .Select(d => d.DeviceSerialNo)
                    .ToHashSet();
                foreach (var orphan in LoggingManager.Instance.SubscribedChannels
                    .Where(c => !stillConnected.Contains(c.DeviceSerialNo))
                    .ToList())
                {
                    LoggingManager.Instance.Unsubscribe(orphan);
                }
                break;
            case "SubscribedChannels":
                ActiveChannels.Clear();
                ActiveInputChannels.Clear();

                // Sort channels naturally by name before adding to collections
                var sortedChannels = LoggingManager.Instance.SubscribedChannels
                    .NaturalOrderBy(channel => channel.Name);

                foreach (var channel in sortedChannels)
                {
                    if (!channel.IsOutput) // Condition changed to remove IsVisible filter
                    {
                        ActiveInputChannels.Add(channel);
                    }
                    ActiveChannels.Add(channel);
                }
                break;
            case nameof(LoggingManager.LoggingSessions):
                AttachLoggingSessionsCollection(LoggingManager.Instance.LoggingSessions);
                NotifyLoggingSessionsChanged();
                break;
            case "NotificationCount":
                var data = _versionNotification;
                NotificationCount = data.NotificationCount;
                if (NotificationCount > 0)
                {
                    VersionName = data.VersionNumber;
                    var notify = new Notifications
                    {
                        IsFirmwareUpdate = false,
                        DeviceSerialNo = null,
                        Message = $"Please update latest application version:  {VersionName}",
                        Link = "https://github.com/daqifi/daqifi-desktop/releases"
                    };
                    if (!NotificationList.Any(n => n.Message == notify.Message || n.Link == notify.Link))
                    {
                        NotificationList.Add(notify);
                    }
                }
                break;
            case "NotifyConnection":
                var deviceConnection = ConnectionManager.Instance.NotifyConnection;
                if (deviceConnection)
                {
                    var errorDialogViewModel = new ErrorDialogViewModel("Device disconnected unexpectedly.");
                    _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
                    ConnectionManager.Instance.NotifyConnection = false;
                }
                break;
        }
        CanToggleLogging = ActiveChannels.Count > 0;
        _ = GetFirmwareupdatationList();
        RemoveNotification();
    }
    public async Task<MessageDialogResult> ShowMessage(string title, string message, MessageDialogStyle dialogStyle)
    {
        var metroWindow = Application.Current.MainWindow as MetroWindow;
        return await metroWindow.ShowMessageAsync(title, message, dialogStyle, metroWindow.MetroDialogOptions);
    }

    /// <summary>
    /// Displays the in-pane dark confirm overlay and returns true if the user
    /// chose the affirmative button. Bound by the LoggedDataPane confirm overlay
    /// via <see cref="IsConfirmOpen"/>, <see cref="ConfirmTitle"/>,
    /// <see cref="ConfirmMessage"/>, and <see cref="ConfirmAffirmativeLabel"/>;
    /// the two button commands complete the underlying
    /// <see cref="TaskCompletionSource{TResult}"/>.
    /// </summary>
    private Task<bool> ShowConfirm(
        string title,
        string message,
        string affirmativeLabel = "OK",
        bool isDestructive = false)
    {
        // Defensive: if a prior confirm is somehow still pending, cancel it
        // with a negative so the previous awaiter unwinds cleanly.
        _confirmTcs?.TrySetResult(false);

        _confirmTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ConfirmTitle = title;
        ConfirmMessage = message;
        ConfirmAffirmativeLabel = affirmativeLabel;
        ConfirmAffirmativeIsDestructive = isDestructive;
        IsConfirmOpen = true;
        return _confirmTcs.Task;
    }

    private void CompleteConfirm(bool result)
    {
        IsConfirmOpen = false;
        var tcs = _confirmTcs;
        _confirmTcs = null;
        tcs?.TrySetResult(result);
    }
    public void CloseFlyouts()
    {
        IsDeviceSettingsOpen = false;
        IsLoggingSessionSettingsOpen = false;
        IsLiveGraphSettingsOpen = false;
        IsLogSummaryOpen = false;
        IsNotificationsOpen = false;
        IsFirmwareUpdatationFlyoutOpen = false;
    }

    private bool CanExportAllLoggingSession()
    {
        return LoggingSessions.Count > 0;
    }

    /// <summary>
    /// Handles debug mode toggle changes
    /// </summary>
    partial void OnIsDebugModeEnabledChanged(bool value)
    {
        if (value)
        {
            _appLogger.Information("[DEBUG_MODE] Debug mode enabled - detailed diagnostics will be logged");
            DebugData.Clear();
        }
        else
        {
            _appLogger.Information("[DEBUG_MODE] Debug mode disabled");
        }

        // Notify all connected devices about debug mode change
        foreach (var device in ConnectedDevices)
        {
            if (device is AbstractStreamingDevice streamingDevice)
            {
                streamingDevice.SetDebugMode(value);
            }
        }
    }

    /// <summary>
    /// Command to toggle debug mode
    /// </summary>
    [RelayCommand]
    private void ToggleDebugMode()
    {
        IsDebugModeEnabled = !IsDebugModeEnabled;
    }

    /// <summary>
    /// Command to clear debug data
    /// </summary>
    [RelayCommand]
    private void ClearDebugData()
    {
        DebugData.Clear();
        _appLogger.Information("[DEBUG_MODE] Debug data cleared");
    }

    /// <summary>
    /// Command to open debug window
    /// </summary>
    [RelayCommand]
    private void OpenDebugWindow()
    {
        var debugWindow = new DebugWindow(this);
        debugWindow.Show();
    }

    /// <summary>
    /// Handles debug data received from devices
    /// </summary>
    private void OnDebugDataReceived(DebugDataModel debugData)
    {
        DebugData.AddEntry(debugData);
    }

    #endregion

    #region Disk Space Monitoring

    /// <summary>
    /// Disposes disk space monitoring resources. Call on application shutdown.
    /// </summary>
    public void DisposeDiskSpaceMonitor()
    {
        if (_diskSpaceMonitor == null)
        {
            return;
        }

        _diskSpaceMonitor.LowSpaceWarning -= OnDiskSpaceLowWarning;
        _diskSpaceMonitor.CriticalSpaceReached -= OnDiskSpaceCritical;
        _diskSpaceMonitor.Dispose();
        _diskSpaceMonitor = null;
    }

    private void OnDiskSpaceLowWarning(object? sender, DiskSpaceEventArgs e)
    {
        // BeginInvoke (async) to avoid blocking the timer thread
        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            _ = ShowDiskSpaceMessage(
                "Low Disk Space Warning",
                $"Only {e.AvailableMegabytes} MB of disk space remaining. " +
                "Logging will be stopped automatically if space drops below 50 MB.\n\n" +
                "Consider freeing disk space by deleting old logging sessions or removing other files.");
        });
    }

    private void OnDiskSpaceCritical(object? sender, DiskSpaceEventArgs e)
    {
        // BeginInvoke (async) to avoid blocking the timer thread
        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            _appLogger.Warning($"Disk space critical ({e.AvailableMegabytes} MB) — automatically stopping logging");
            IsLogging = false;
            OnPropertyChanged(nameof(IsLogging));

            _ = ShowDiskSpaceMessage(
                "Logging Stopped — Disk Space Critical",
                $"Logging was automatically stopped because disk space dropped to {e.AvailableMegabytes} MB.\n\n" +
                "To prevent system instability, logging has been halted. " +
                "Please free disk space by deleting old logging sessions or removing other files before resuming.");
        });
    }

    private async Task ShowDiskSpaceMessage(string title, string message)
    {
        try
        {
            if (Application.Current?.MainWindow is MetroWindow metroWindow)
            {
                await metroWindow.ShowMessageAsync(title, message, MessageDialogStyle.Affirmative, metroWindow.MetroDialogOptions);
            }
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Failed to show disk space warning dialog");
        }
    }

    #endregion
}
