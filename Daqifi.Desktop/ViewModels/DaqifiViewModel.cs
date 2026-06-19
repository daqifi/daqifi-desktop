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
using Daqifi.Desktop.Device.Firmware;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Application = System.Windows.Application;
using CommunityToolkit.Mvvm.Input;
using Daqifi.Core.Device.SdCard;

namespace Daqifi.Desktop.ViewModels;

public partial class DaqifiViewModel : ObservableObject, IFirmwareUpdateHost, ILoggingSessionListHost, IDiskSpaceMonitorHost, IDisposable
{
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

    // In-pane confirm overlay (delete confirmations, etc.). The reusable ConfirmOverlayViewModel
    // owns the bound state, the affirmative/negative commands, and the awaitable ShowAsync
    // (issue #592); the LoggedDataPane overlay binds ConfirmOverlay.*. It replaces the MahApps
    // MessageDialog (white card / blue theme) which clashed with the dark, tile-based design system.

    /// <summary>
    /// Backs the in-pane confirm overlay. Bound by the LoggedDataPane confirm overlay via
    /// <c>ConfirmOverlay.IsOpen</c> / <c>ConfirmOverlay.Title</c> / <c>ConfirmOverlay.Message</c> /
    /// <c>ConfirmOverlay.AffirmativeLabel</c> / <c>ConfirmOverlay.AffirmativeIsDestructive</c> and
    /// the affirmative/negative button commands.
    /// </summary>
    public ConfirmOverlayViewModel ConfirmOverlay { get; } = new();

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
    private readonly IDbContextFactory<LoggingContext>? _loggingContextFactory;
    private readonly FirmwareUpdateCoordinator _firmwareCoordinator;
    private readonly LoggingSessionListViewModel _loggingSessionList;
    private ConnectionDialogViewModel _connectionDialogViewModel;
    private string _selectedLoggingMode = "Stream to App";
    private bool _isLogToDeviceMode;
    private SdCardLogFormat _selectedSdCardLogFormat = SdCardLogFormat.Protobuf;

    // Disk-space gating + monitoring (pre-logging check, in-session monitor, low/critical handling)
    // lives in the coordinator (issue #592). Created during window init; null in non-window-init
    // construction paths, so callers null-check it exactly as the previous monitor field was.
    private DiskSpaceMonitorCoordinator? _diskSpaceCoordinator;
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
    /// Gets or sets whether a logging session is active.
    /// <para>
    /// The getter returns true if the user has toggled logging on, OR if any connected
    /// device reports it is actively logging to its SD card. Reading from device state
    /// ensures the value reflects reality even when SD-card logging was started in a
    /// prior session and the device kept logging across a desktop reconnect. Streaming-
    /// mode state is not tracked here because <c>IsStreaming</c> is not on the
    /// <see cref="IStreamingDevice"/> interface; the streaming path updates state
    /// synchronously through the setter, so the getter only needs to supplement that
    /// with the SD-card signal.
    /// </para>
    /// <para>
    /// The setter is the single entry point for starting/stopping logging: it gates
    /// startup on available disk space, starts or stops disk-space monitoring, and
    /// starts or stops streaming (or SD-card logging) on every connected device. It
    /// also raises a change notification so all bindings — the logging toggle, the
    /// "LOGGING ON/OFF" status label, and the LIVE/MODE/RATE header chips — reflect the
    /// current session state.
    /// </para>
    /// </summary>
    public bool IsLogging
    {
        get => _isLogging || AnyDeviceActivelyLogging();
        set
        {
            // Pre-logging disk-space gate lives in the coordinator: it blocks the start (and shows the
            // appropriate dialog) when the disk is critically low, or warns when low-but-not-critical.
            var preSessionWarningShown = false;
            if (value && _diskSpaceCoordinator != null)
            {
                var decision = _diskSpaceCoordinator.EvaluateStartLogging();
                if (!decision.CanStart)
                {
                    // Notify bindings so TwoWay toggle reverts to false
                    OnPropertyChanged(nameof(IsLogging));
                    return;
                }

                preSessionWarningShown = decision.SuppressInitialWarning;
            }

            _isLogging = value;
            // Notify bindings on the normal start/stop path. The toggle is the binding
            // source so it flips on its own, but the "LOGGING ON/OFF" status label and
            // the LIVE/MODE/RATE header chips are consumers that only refresh on this
            // change notification — without it the label goes stale (toggle reads On
            // while the label still says "LOGGING OFF").
            OnPropertyChanged(nameof(IsLogging));
            LoggingManager.Instance.Active = value;
            if (_isLogging)
            {
                _diskSpaceCoordinator?.StartMonitoring(suppressInitialWarning: preSessionWarningShown);

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
                _diskSpaceCoordinator?.StopMonitoring();

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
            ConfirmOverlay.Cancel();
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
        _loggingContextFactory = loggingContextFactory;

        // Firmware update orchestration and version-checking live in the coordinator (issue #592).
        // The view model is the composition root: it builds the production service defaults here when
        // DI supplied none, so the coordinator itself never news up service clients or reaches into
        // singletons. The view model keeps only the bound properties + thin command bindings and
        // feeds the coordinator through the IFirmwareUpdateHost seam (which it implements below).
        var firmwareDownload = firmwareDownloadService ?? CreateDefaultFirmwareDownloadService();
        var resolvedFirmwareLogger = firmwareLogger ?? NullLogger<FirmwareUpdateService>.Instance;
        var firmwareUpdate = firmwareUpdateService
            ?? CreateDefaultFirmwareUpdateService(firmwareDownload, resolvedFirmwareLogger);
        _firmwareCoordinator = new FirmwareUpdateCoordinator(
            this,
            firmwareUpdate,
            firmwareDownload,
            resolvedFirmwareLogger,
            _appLogger,
            App.DaqifiDataDirectory,
            wifiFirmwareUpdateServiceFactory);

        // Logged-data session-list actions (display/export/delete + collection plumbing) live in the
        // list view model (issue #592). The view model keeps the bound properties + thin command
        // bindings and feeds the list through the ILoggingSessionListHost seam (implemented below);
        // the storage purge collaborators are injected here so the list never reaches into
        // App.DatabasePath / App.ServiceProvider directly.
        _loggingSessionList = new LoggingSessionListViewModel(
            this,
            GetLoggingContextFactory,
            App.DatabasePath,
            _appLogger);

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
                    _loggingSessionList.AttachCollection(LoggingManager.Instance.LoggingSessions);
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

                    // Disk space gating + monitoring. The view model is the composition root: it builds
                    // the production monitor here and hands it to the coordinator, which owns the
                    // pre-logging gate, the in-session monitor, and the low/critical handling and reaches
                    // back through the IDiskSpaceMonitorHost seam (implemented below) to stop logging and
                    // present dialogs.
                    _diskSpaceCoordinator = new DiskSpaceMonitorCoordinator(
                        this,
                        new DiskSpaceMonitor(App.DaqifiDataDirectory),
                        _appLogger);

                    if (LoggingManager.Instance.LoggingSessions.Count == 0)
                    {
                        LoggingManager.Instance.ReloadPersistedLoggingSessions();
                    }

                    // Configure default grid lines
                    Plotter.ShowingMinorXAxisGrid = false;
                    Plotter.ShowingMinorYAxisGrid = false;

                    // Firewall configuration requires administrator rights. Only attempt it
                    // on an elevated, non-test launch (production). Un-elevated runs (the UI
                    // test harness or a normal non-admin Debug launch) cannot change firewall
                    // rules and would otherwise just surface a modal "configure manually"
                    // prompt, so skip it entirely.
                    if (App.IsElevated && !App.IsTestMode)
                    {
                        FirewallConfiguration.InitializeFirewallRules();
                    }
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

    /// <summary>
    /// Builds the production firmware download service used when DI supplies none. Lives here at the
    /// composition root so <see cref="FirmwareUpdateCoordinator"/> receives a ready-made instance
    /// rather than constructing its own service clients.
    /// </summary>
    private static IFirmwareDownloadService CreateDefaultFirmwareDownloadService()
    {
        return new GitHubFirmwareDownloadService(new HttpClient());
    }

    /// <summary>
    /// Builds the production PIC32 firmware update service used when DI supplies none.
    /// </summary>
    private static IFirmwareUpdateService CreateDefaultFirmwareUpdateService(
        IFirmwareDownloadService firmwareDownloadService,
        ILogger<FirmwareUpdateService> logger)
    {
        return new FirmwareUpdateService(
            FirmwareUpdateServiceConfig.CreateBootloaderHidTransport(),
            firmwareDownloadService,
            new ProcessExternalProcessRunner(),
            logger,
            options: FirmwareUpdateServiceConfig.CreateOptions());
    }

    #endregion

    #region Register Command

    private void RegisterCommands()
    {
        DeleteLoggingSessionCommand = new AsyncRelayCommand<LoggingSession?>(_loggingSessionList.DeleteSessionAsync);
        DeleteAllLoggingSessionCommand = new AsyncRelayCommand(_loggingSessionList.DeleteAllSessionsAsync, CanDeleteAllLoggingSession);
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
    private Task UploadFirmware() => _firmwareCoordinator.UploadFirmwareAsync();

    [RelayCommand(CanExecute = nameof(CanCancelFirmwareUpload))]
    private void CancelFirmwareUpload() => _firmwareCoordinator.CancelUpload();

    private bool CanCancelFirmwareUpload()
    {
        return IsFirmwareUploading;
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
        _firmwareCoordinator.RemoveFirmwareNotification(deviceToDisconnect);

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

    // Thin command bindings: the logged-data session-list logic lives in _loggingSessionList
    // (issue #592). XAML still binds DisplayLoggingSessionCommand / ExportLoggingSessionCommand /
    // ExportAllLoggingSessionCommand on this view model, so DataContext bindings are unchanged.
    [RelayCommand]
    private Task DisplayLoggingSession(LoggingSession? session) => _loggingSessionList.DisplaySessionAsync(session);

    [RelayCommand]
    private Task ExportLoggingSession(LoggingSession? session) => _loggingSessionList.ExportSessionAsync(session);

    [RelayCommand(CanExecute = nameof(CanExportAllLoggingSession))]
    private Task ExportAllLoggingSession() => _loggingSessionList.ExportAllSessionsAsync();

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

            var result = await Task.Run(() =>
                importer.ImportFromFileAsync(dialog.FileName, null, progress, CancellationToken.None));

            Application.Current.Dispatcher.Invoke(() =>
            {
                LoggingManager.Instance.LoggingSessions.Add(result.Session);
            });

            var message = $"Successfully imported {System.IO.Path.GetFileName(dialog.FileName)}";
            var timestampWarning = result.TimestampQuality.BuildUserWarning();
            if (timestampWarning != null)
            {
                message += $"\n\nWarning: {timestampWarning}";
            }

            await ShowMessage("Import Complete", message, MessageDialogStyle.Affirmative);
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

    private IDbContextFactory<LoggingContext> GetLoggingContextFactory()
    {
        return _loggingContextFactory
            ?? App.ServiceProvider?.GetService<IDbContextFactory<LoggingContext>>()
            ?? throw new InvalidOperationException("Logging context factory is not available.");
    }

    /// <summary>
    /// Re-raises change notifications for the bound session collection and refreshes the
    /// "export all" / "delete all" command CanExecute state. Internal callers (the
    /// <see cref="UpdateUi"/> property-changed handler) invoke this directly; the
    /// <see cref="ILoggingSessionListHost"/> seam adds UI-thread marshalling for the list view model.
    /// </summary>
    private void NotifyLoggingSessionsChanged()
    {
        OnPropertyChanged(nameof(LoggingSessions));
        OnPropertyChanged(nameof(HasLoggingSessions));
        DeleteAllLoggingSessionCommand?.NotifyCanExecuteChanged();
        ExportAllLoggingSessionCommand.NotifyCanExecuteChanged();
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

    /// <summary>
    /// The latest published firmware version, surfaced for display. The firmware coordinator
    /// owns the value and performs the underlying version check via
    /// <see cref="FirmwareUpdateCoordinator.RefreshFirmwareUpdatesAsync"/>.
    /// </summary>
    public string LatestFirmwareVersionText => _firmwareCoordinator.LatestFirmwareVersion;

    /// <summary>
    /// Prunes notifications whose owning device is no longer connected. General-purpose
    /// (covers both app-version and firmware notifications); firmware-specific add/remove
    /// now lives in the firmware coordinator.
    /// </summary>
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

    #endregion

    [RelayCommand]
    private void OpenNotifications()
    {
        IsNotificationsOpen = true;
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
            var latestFirmwareVersion = _firmwareCoordinator.LatestFirmwareVersion;
            if (!string.IsNullOrEmpty(latestFirmwareVersion))
            {
                connectedDevice.IsFirmwareOutdated = VersionHelper.Compare(DeviceVersion, latestFirmwareVersion) < 0;
            }

            ConnectedDevices.Add(connectedDevice);

            // Sync SD card log format so newly connected devices match the UI selection
            connectedDevice.SdCardLogFormat = _selectedSdCardLogFormat;

            // Apply the current debug-mode toggle to streaming devices. The DebugDataReceived
            // subscription itself is managed centrally in OnConnectedDevicesCollectionChanged (wired
            // when the device is added to ConnectedDevices above, unwired when it's removed), so it
            // stays symmetric across reconnects instead of accumulating duplicate handlers.
            if (connectedDevice is AbstractStreamingDevice streamingDevice)
            {
                streamingDevice.SetDebugMode(IsDebugModeEnabled);
            }
        }

        return Task.CompletedTask;
    }

    // Devices we've wired per-VM event handlers onto — both the logging-state PropertyChanged handler
    // and (for streaming devices) the DebugDataReceived handler. Tracked as one set so subscribe and
    // unsubscribe stay symmetric across Reset/Replace/add/remove and so Dispose can tear them all down.
    private readonly HashSet<IStreamingDevice> _subscribedDevices = [];

    private void OnConnectedDevicesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // ObservableCollection.Clear() raises a Reset with OldItems == null, so we
        // can't rely on the args alone — diff against our tracked-subscription set
        // to handle Reset, Replace, and per-item adds/removes uniformly.
        var current = new HashSet<IStreamingDevice>(ConnectedDevices);

        foreach (var stale in _subscribedDevices.Except(current).ToList())
        {
            UnsubscribeDeviceEvents(stale);
            _subscribedDevices.Remove(stale);
        }

        foreach (var added in current.Except(_subscribedDevices).ToList())
        {
            SubscribeDeviceEvents(added);
            _subscribedDevices.Add(added);
        }

        RaiseLoggingStateChanged();
    }

    /// <summary>
    /// Wires up the per-device handlers this view model owns: the logging-state change handler and,
    /// for streaming devices, the debug-data handler. Paired with <see cref="UnsubscribeDeviceEvents"/>
    /// and driven from <see cref="OnConnectedDevicesCollectionChanged"/> so the two subscriptions are
    /// added and removed together (issue #592).
    /// </summary>
    private void SubscribeDeviceEvents(IStreamingDevice device)
    {
        device.PropertyChanged += OnDeviceLoggingStateChanged;
        if (device is AbstractStreamingDevice streamingDevice)
        {
            streamingDevice.DebugDataReceived += OnDebugDataReceived;
        }
    }

    /// <summary>
    /// Removes the per-device handlers wired by <see cref="SubscribeDeviceEvents"/>. Called when a
    /// device leaves <c>ConnectedDevices</c> and on <see cref="Dispose"/>, so a removed (or
    /// reconnecting) device never leaves a dangling debug subscription that keeps this view model
    /// rooted or fires after teardown.
    /// </summary>
    private void UnsubscribeDeviceEvents(IStreamingDevice device)
    {
        device.PropertyChanged -= OnDeviceLoggingStateChanged;
        if (device is AbstractStreamingDevice streamingDevice)
        {
            streamingDevice.DebugDataReceived -= OnDebugDataReceived;
        }
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
                _loggingSessionList.AttachCollection(LoggingManager.Instance.LoggingSessions);
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
        _ = _firmwareCoordinator.RefreshFirmwareUpdatesAsync();
        RemoveNotification();
    }
    public async Task<MessageDialogResult> ShowMessage(string title, string message, MessageDialogStyle dialogStyle)
    {
        var metroWindow = Application.Current.MainWindow as MetroWindow;
        return await metroWindow.ShowMessageAsync(title, message, dialogStyle, metroWindow.MetroDialogOptions);
    }

    /// <summary>
    /// Displays the in-pane dark confirm overlay and returns true if the user chose the affirmative
    /// button. Thin forwarder to <see cref="ConfirmOverlay"/> (issue #592); the existing
    /// <see cref="ILoggingSessionListHost.ShowConfirmAsync"/> caller stays unchanged.
    /// </summary>
    private Task<bool> ShowConfirm(
        string title,
        string message,
        string affirmativeLabel = "OK",
        bool isDestructive = false)
        => ConfirmOverlay.ShowAsync(title, message, affirmativeLabel, isDestructive);

    public void CloseFlyouts()
    {
        IsDeviceSettingsOpen = false;
        IsLoggingSessionSettingsOpen = false;
        IsLiveGraphSettingsOpen = false;
        IsLogSummaryOpen = false;
        IsNotificationsOpen = false;
        IsFirmwareUpdatationFlyoutOpen = false;
    }

    #region IFirmwareUpdateHost implementation

    // The firmware coordinator reaches the view model's bound progress/status properties
    // (SelectedDevice, FirmwareFilePath, IsFirmwareUploading, UploadFirmwareProgress, etc.)
    // through their existing public [ObservableProperty] members, which already satisfy the
    // IFirmwareUpdateHost setters implicitly. Only the members below need an explicit bridge
    // because they map onto differently-named state or onto desktop singletons the coordinator
    // intentionally never touches directly.

    /// <summary>
    /// Devices the firmware version check iterates. Sourced from <see cref="ConnectionManager"/>
    /// (not the UI-facing <see cref="ConnectedDevices"/> collection) to exactly preserve the
    /// pre-refactor behavior.
    /// </summary>
    IReadOnlyList<IStreamingDevice> IFirmwareUpdateHost.ConnectedDevices =>
        ConnectionManager.Instance.ConnectedDevices;

    /// <summary>The shared, bound notification collection the coordinator mutates.</summary>
    ObservableCollection<Notifications> IFirmwareUpdateHost.Notifications => NotificationList;

    /// <summary>Routes the in-progress-update flag to the connection manager.</summary>
    IStreamingDevice? IFirmwareUpdateHost.DeviceBeingUpdated
    {
        set => ConnectionManager.Instance.DeviceBeingUpdated = value;
    }

    /// <summary>Re-syncs the notification badge after the coordinator adds/removes a notification.</summary>
    void IFirmwareUpdateHost.RefreshNotificationCount() => NotificationCount = NotificationList.Count;

    /// <summary>
    /// Presents a firmware error dialog on the UI thread. Dialog presentation is a view concern,
    /// so the coordinator delegates it here and stays free of WPF dependencies.
    /// </summary>
    void IFirmwareUpdateHost.ShowFirmwareError(string message)
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

    /// <summary>
    /// Presents the firmware-update success dialog on the UI thread and closes the firmware flyout.
    /// </summary>
    void IFirmwareUpdateHost.ShowFirmwareUpdateSucceeded()
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

    #region ILoggingSessionListHost implementation

    // The list view model reaches the view model's bound logged-data state (SelectedLoggingSession,
    // IsLoggedDataBusy, LoggedDataBusyReason, LoggingSessions) through their existing public members,
    // which already satisfy the ILoggingSessionListHost setters/getters implicitly. Only the members
    // below need an explicit bridge because they map onto DbLogger, desktop singletons, or WPF the
    // list view model intentionally never touches directly.

    /// <summary>True while logging is active. Sourced from <see cref="LoggingManager"/>.</summary>
    bool ILoggingSessionListHost.IsLoggingActive => LoggingManager.Instance.Active;

    /// <summary>
    /// Re-raises the session-collection notifications on the UI thread. The list view model calls this
    /// off the UI thread (from a collection-changed callback or after a background delete), so this
    /// folds in the dispatcher marshalling the old collection-changed handler performed.
    /// </summary>
    void ILoggingSessionListHost.NotifyLoggingSessionsChanged()
    {
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            _ = dispatcher.InvokeAsync(NotifyLoggingSessionsChanged);
            return;
        }

        NotifyLoggingSessionsChanged();
    }

    void ILoggingSessionListHost.DisplaySessionOnPlot(LoggingSession session) => DbLogger.DisplayLoggingSession(session);

    void ILoggingSessionListHost.DeleteSessionFromDatabase(LoggingSession session) => DbLogger.DeleteLoggingSession(session);

    void ILoggingSessionListHost.ClearPlot() => DbLogger.ClearPlot();

    void ILoggingSessionListHost.SuspendConsumer() => DbLogger.SuspendConsumer();

    void ILoggingSessionListHost.ResumeConsumer() => DbLogger.ResumeConsumer();

    void ILoggingSessionListHost.ClearBuffer() => DbLogger.ClearBuffer();

    void ILoggingSessionListHost.DiscardPendingBatch() => DbLogger.DiscardPendingBatch();

    /// <summary>
    /// Builds and presents the single-session export dialog. Kept on the host because the dialog view
    /// model resolves services from the desktop container and presentation is a WPF concern.
    /// </summary>
    async Task ILoggingSessionListHost.ShowExportDialogForSessionAsync(int sessionId)
    {
        var exportDialogViewModel = new ExportDialogViewModel(sessionId);

        // UI-test hook: when DAQIFI_TEST_EXPORT_PATH is set, export straight to that directory
        // with no SaveFileDialog (mirrors DAQIFI_TEST_MODE / AppDataPaths). Unset in production,
        // where the interactive dialog below is used unchanged.
        if (Common.AppDataPaths.TestExportPath != null)
        {
            await exportDialogViewModel.ExportToDirectoryAsync(Common.AppDataPaths.TestExportPath);
            return;
        }

        _dialogService.ShowDialog<ExportDialog>(this, exportDialogViewModel);
    }

    /// <summary>Builds and presents the "export all" dialog for the supplied sessions.</summary>
    async Task ILoggingSessionListHost.ShowExportDialogForSessionsAsync(IReadOnlyList<LoggingSession> sessions)
    {
        var exportDialogViewModel = new ExportDialogViewModel(sessions);

        // UI-test hook: see ShowExportDialogForSessionAsync above. Same seam, so "Export All" is
        // equally dialog-free and deterministic under automation when the env var is set.
        if (Common.AppDataPaths.TestExportPath != null)
        {
            await exportDialogViewModel.ExportToDirectoryAsync(Common.AppDataPaths.TestExportPath);
            return;
        }

        _dialogService.ShowDialog<ExportDialog>(this, exportDialogViewModel);
    }

    /// <summary>Routes the list view model's confirmations through the in-pane confirm overlay.</summary>
    Task<bool> ILoggingSessionListHost.ShowConfirmAsync(string title, string message, string affirmativeLabel, bool isDestructive)
        => ShowConfirm(title, message, affirmativeLabel, isDestructive);

    /// <summary>Routes the list view model's informational messages through the MahApps message dialog.</summary>
    Task ILoggingSessionListHost.ShowMessageAsync(string title, string message)
        => ShowMessage(title, message, MessageDialogStyle.Affirmative);

    #endregion

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

    // IDiskSpaceMonitorHost implementation. The coordinator owns the gate/monitor/event logic and
    // reaches back here only to stop the session and present dialogs — both of which are WPF concerns
    // and must be marshalled to the UI thread because the monitor's threshold events fire on its
    // background timer thread.

    /// <summary>
    /// Stops the active logging session in response to critically-low disk space. Marshalled to the UI
    /// thread without blocking the monitor's timer thread (the previous handler used BeginInvoke).
    /// </summary>
    void IDiskSpaceMonitorHost.StopLogging()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            // No UI thread to marshal to (shutdown / non-WPF host). The IsLogging setter raises
            // PropertyChanged and iterates the bound ConnectedDevices, so it must not run on the
            // monitor's background timer thread — matches the original BeginInvoke, which no-op'd
            // when the dispatcher was unavailable.
            return;
        }

        if (dispatcher.CheckAccess())
        {
            IsLogging = false;
            return;
        }

        dispatcher.BeginInvoke(() => IsLogging = false);
    }

    /// <summary>
    /// Presents a disk-space dialog. The pre-logging gate calls this on the UI thread; the low/critical
    /// events call it from the monitor's timer thread, so off-thread calls are marshalled (non-blocking,
    /// matching the previous BeginInvoke handlers).
    /// </summary>
    Task IDiskSpaceMonitorHost.ShowDiskSpaceMessageAsync(string title, string message)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => _ = ShowDiskSpaceMessage(title, message));
            return Task.CompletedTask;
        }

        return ShowDiskSpaceMessage(title, message);
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

    #region IDisposable

    private bool _disposed;

    /// <summary>
    /// Tears down the resources and event subscriptions this view model owns. Invoked once when the
    /// main window closes (see <c>MainWindow</c>). Consolidates the previously ad-hoc cleanup into a
    /// single deterministic path (issue #592): the disk-space coordinator, the transient
    /// network-settings status timer, the SD-card elapsed-time timer, and the long-lived singleton /
    /// per-device event subscriptions wired up in the constructor (which would otherwise pin this view
    /// model for the life of the process).
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        // Disk-space gating/monitoring coordinator (owns the IDiskSpaceMonitor and its subscriptions).
        _diskSpaceCoordinator?.Dispose();
        _diskSpaceCoordinator = null;

        // Transient WiFi-settings "applied" status timer.
        CancelAndDisposeNetworkSettingsCts();

        // SD-card elapsed-time DispatcherTimer.
        if (_sdLoggingElapsedTimer != null)
        {
            _sdLoggingElapsedTimer.Stop();
            _sdLoggingElapsedTimer.Tick -= OnSdLoggingTimerTick;
            _sdLoggingElapsedTimer = null;
            _sdLoggingStartedAt = null;
        }

        // Unsubscribe from the long-lived singletons + the VM's own collection so they don't keep this
        // view model alive after the window closes. (-= is a no-op for handlers that were never wired,
        // e.g. in non-window-init construction paths.)
        ConnectionManager.Instance.PropertyChanged -= UpdateUi;
        LoggingManager.Instance.PropertyChanged -= UpdateUi;
        ConnectedDevices.CollectionChanged -= OnConnectedDevicesCollectionChanged;

        // Per-device subscriptions (logging-state PropertyChanged + debug-data) wired up as devices
        // are added in OnConnectedDevicesCollectionChanged. Left subscribed, a device keeps this VM
        // rooted and a late debug callback would marshal onto the UI thread during shutdown
        // (OnDebugDataReceived -> DebugData.AddEntry uses Dispatcher.Invoke).
        foreach (var device in _subscribedDevices)
        {
            UnsubscribeDeviceEvents(device);
        }
        _subscribedDevices.Clear();

        // The session-list view model observes the singleton LoggingManager.LoggingSessions collection.
        _loggingSessionList.DetachCollection();

        GC.SuppressFinalize(this);
    }

    #endregion
}
