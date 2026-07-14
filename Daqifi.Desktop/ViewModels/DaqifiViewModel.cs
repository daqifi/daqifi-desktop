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
using Daqifi.Desktop.Device.SerialDevice;
using ILanChipInfoProvider = Daqifi.Core.Firmware.ILanChipInfoProvider;
using LanChipInfo = Daqifi.Core.Firmware.LanChipInfo;
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
    [NotifyCanExecuteChangedFor(nameof(UpdateWifiFirmwareOnlyCommand))]
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
    [NotifyCanExecuteChangedFor(nameof(UpdateWifiFirmwareOnlyCommand))]
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

    /// <summary>
    /// Keys (serial number, falling back to COM port) of USB devices whose WiFi firmware
    /// has already been probed this connection. Prevents the connect-time probe — which
    /// powers on the WiFi module and can take several seconds — from re-running on every
    /// UI refresh. Entries are pruned when their device disconnects. Case-insensitive to
    /// match the casing used elsewhere for serial numbers / COM keys.
    /// </summary>
    private const int WifiChipInfoMaxAttempts = 3;
    private static readonly TimeSpan WifiChipInfoRetryDelay = TimeSpan.FromSeconds(2);

    private readonly HashSet<string> _wifiFirmwareCheckedDevices = new(StringComparer.OrdinalIgnoreCase);

    // Cancels an in-flight WiFi firmware probe. A probe powers on the WiFi module and runs a
    // multi-second SCPI exchange; if a firmware flash starts while one is in flight it corrupts
    // the bootloader handshake, so the flash commands cancel it before touching the device.
    private CancellationTokenSource? _wifiCheckCts;

    // Guards against overlapping probes. CheckWifiFirmwareAsync is fire-and-forget from the
    // ConnectedDevices change handler (UI thread); a second change firing before the first probe's
    // awaits finish would race on the cache / dispose the in-flight CTS out from under it.
    // 0 = idle, 1 = a probe pass is running. Interlocked single-flight guard (see CheckWifiFirmwareAsync).
    private int _wifiCheckInProgress;

    // The currently-running probe task (null when none). A flash awaits this after cancelling so an
    // in-flight POWer:STATe 1 / GETChipInfo? exchange is fully unwound before the device enters WiFi
    // update mode — cancelling the token alone does NOT abort an SCPI write already on the wire, and
    // any byte that lands while the WINC is bridging corrupts the program (bricks the module).
    private Task? _wifiProbeTask;

    // Owns the WiFi-only flash lifecycle (the PIC32 path's CTS lives in the coordinator).
    private CancellationTokenSource? _firmwareUploadCts;
    private readonly LoggingSessionListViewModel _loggingSessionList;
    private ConnectionDialogViewModel? _connectionDialogViewModel;
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
            wifiFirmwareUpdateServiceFactory,
            // Suspend the app-global bootloader watcher's discovery during the PIC32 flash so it doesn't
            // grab the connected device when it reboots into the bootloader mid-update.
            watcher: App.ServiceProvider?.GetService<IBootloaderWatcher>());

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
            // One-time host initialization: build the loggers (including the dark-themed Plotter),
            // register commands, and wire the long-lived singleton subscriptions. This runs on the
            // FIRST construction — the live view model is the one bound to the window (see
            // MainWindow.xaml.cs), so deferring init to a hypothetical later construction would leave
            // Plotter null and the Live Graph PlotView would paint OxyPlot's default white background in
            // the empty state. The guard flag is set only after the body completes (see end of the try),
            // so a failed init is not marked "done": doing so would strand Plotter/DbLogger null while
            // blocking any retry, and DbLogger is dereferenced unconditionally via ILoggingSessionListHost.
            if (!app.IsWindowInit)
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

                    // Mark host init complete only after every step above succeeded, so a partial
                    // failure leaves the flag clear (retryable) instead of permanently stranding the
                    // loggers that XAML bindings and ILoggingSessionListHost depend on.
                    app.IsWindowInit = true;
                }
                catch (Exception ex)
                {
                    _appLogger.Error(ex, "DaqifiViewModel");
                }
            }
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
        var dialogVm = new ConnectionDialogViewModel();
        _connectionDialogViewModel = dialogVm;
        try
        {
            dialogVm.StartConnectionFinders();
            _dialogService.ShowDialog<ConnectionDialog>(this, dialogVm);
        }
        finally
        {
            // Release the field reference before closing so the closed VM can be collected and a Close()
            // side effect can't re-enter through the field. Guarantee the dialog unsubscribes from the
            // app-global watcher's collection even if the dialog threw before its window opened (otherwise
            // the lifetime singleton would permanently root the VM). Close() is idempotent, so the normal
            // window-Closing path that already called it is unaffected; best-effort so it can't mask an
            // exception from the show attempt.
            _connectionDialogViewModel = null;
            try
            {
                dialogVm.Close();
            }
            catch (Exception ex)
            {
                _appLogger.Error(ex, "Failed to close connection dialog view model after dialog attempt.");
            }
        }
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

    /// <summary>
    /// Starts the WiFi version probe for the connected USB devices and tracks the running task so a
    /// subsequent flash can await it draining (see <see cref="QuiesceWifiFirmwareProbeAsync"/>) — that
    /// tracking is load-bearing for the "no SCPI on the bridging WINC during a flash" guarantee.
    /// Triggered on connect and when debug mode is enabled (the probe itself is debug-gated and a no-op
    /// otherwise). All probe-start paths must go through here, never a bare <c>CheckWifiFirmwareAsync()</c>.
    /// </summary>
    private void TriggerWifiFirmwareProbe()
    {
        // The FLASH WIFI button's CanExecute reads SelectedDevice.HasWincWifiModule, which derives from
        // DeviceType — populated asynchronously on connect — so refresh it here (the probe result also
        // re-notifies once it lands).
        UpdateWifiFirmwareOnlyCommand.NotifyCanExecuteChanged();

        var probe = CheckWifiFirmwareAsync();
        // The re-entrancy guard returns an already-completed task when a probe is in progress; only
        // track a task that actually started so the flash's quiesce awaits the real probe.
        if (!probe.IsCompleted)
        {
            _wifiProbeTask = probe;
        }
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
    /// now lives in the firmware coordinator. The app-update notice — the only notification with no
    /// owning device, created with a null serial — is exempt so it survives this cleanup.
    /// </summary>
    internal void RemoveNotification()
    {
        foreach (var notification in NotificationList.ToList())
        {
            // The app-update notice is the one notification with no owning device; it is created with a
            // null serial (see the "NotificationCount" case in UpdateUi). Exempt exactly that — a null
            // serial — or it would be removed on the same UpdateUi pass that adds it (this runs at the
            // end of every UpdateUi) and never appear. Device-owned notifications still go through the
            // disconnect check below, even if a device reports an empty serial.
            if (notification.DeviceSerialNo is null)
            {
                continue;
            }

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
                connectedDevice.IsFirmwareOutdated = FirmwareVersion.Compare(DeviceVersion, latestFirmwareVersion) < 0;
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

        var anyAdded = false;
        foreach (var added in current.Except(_subscribedDevices).ToList())
        {
            SubscribeDeviceEvents(added);
            _subscribedDevices.Add(added);
            anyAdded = true;
        }

        RaiseLoggingStateChanged();

        // NOTE: the WiFi version probe is not fired from this subscription handler. It is
        // centralized in TriggerWifiFirmwareProbe, fired (debug-gated, at most once per device)
        // after a device connects and when debug mode is enabled. The WINC chip-info query it
        // sends (SYSTem:COMMunicate:LAN:GETChipInfo?) can choke a device with a blank/erased WINC —
        // exactly a fresh manufacturing unit needing a flash — which is why it is gated behind
        // debug mode rather than run unconditionally for every device.
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

        // Seed the RATE chip from the device's actual streaming frequency the first time a
        // device connects. SelectedStreamingFrequency's setter is only ever driven by the
        // Devices pane FREQUENCY slider, so without this the chip shows a stale "0 Hz" until
        // the user touches the slider even though the device is already streaming at its
        // default rate (issue #686). This is a read-back, not a user-initiated change, so it
        // intentionally bypasses the setter's logging-lock guard and device write-through.
        if (_selectedStreamingFrequency < 1)
        {
            _selectedStreamingFrequency = device.StreamingFrequency;
            OnPropertyChanged(nameof(SelectedStreamingFrequency));
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

                // Probe the WiFi module version now that the device list (and each device's
                // initialization) has settled — this runs AFTER UpdateConnectedDeviceUI, so it never
                // races the connection handshake. The WiFi version is shown inline on the device pane,
                // so it must be populated on connect. A device with a blank/erased WINC simply times
                // out → "unknown", which is the correct display. The per-connection guard
                // (_wifiFirmwareCheckedDevices) keeps this to one probe each. Go through
                // TriggerWifiFirmwareProbe so the task is tracked for the pre-flash quiesce.
                TriggerWifiFirmwareProbe();
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

    /// <summary>
    /// Cancels any in-flight connect-time WiFi firmware probe AND waits for it to fully unwind before
    /// a flash proceeds. Cancelling the token alone does not abort an SCPI exchange already on the wire,
    /// so the coordinator awaits this before the device enters WiFi update mode — otherwise a stray
    /// <c>POWer:STATe 1</c> / <c>GETChipInfo?</c> can land while the WINC is bridging and brick it.
    /// </summary>
    public async Task QuiesceWifiFirmwareProbeAsync(CancellationToken cancellationToken = default)
    {
        CancelWifiFirmwareCheck();

        var inflight = _wifiProbeTask;
        if (inflight != null)
        {
            // CheckWifiFirmwareAsync already swallows cancellation/probe faults; this await just blocks
            // until the probe's last SCPI exchange has returned (or been cancelled) so nothing overlaps.
            // Honor the caller's token (the firmware-upload CTS) so a user CancelUpload() can interrupt
            // this wait instead of being stuck behind a slow probe unwind.
            try { await inflight.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { /* probe outcome is irrelevant here; we only need it to have stopped touching the device */ }
            _wifiProbeTask = null;
        }
    }

    #endregion

    #region WiFi firmware probe and WiFi-only flash

    /// <summary>
    /// Probes the WiFi module firmware for each connected USB device and flags any whose
    /// firmware is below <see cref="FirmwareUpdateCoordinator.MinimumWifiFirmwareVersion"/> — or whose
    /// chip info cannot be read (<c>SYSTem:COMMunicate:LAN:GETChipInfo?</c> fails) — as needing a
    /// WiFi-only flash. The probe powers on the WiFi module (<c>SYSTem:POWer:STATe 1</c>) and is run at
    /// most once per device connection. Only USB-connected serial devices can be probed or flashed.
    /// </summary>
    private async Task CheckWifiFirmwareAsync()
    {
        // Single-flight the probe: a second ConnectedDevices change before this one's awaits finish
        // would race on _wifiFirmwareCheckedDevices and dispose the in-flight _wifiCheckCts out from
        // under the first probe. Triggers are UI-thread today, but use Interlocked so the guard holds
        // even if a probe continuation resumes off the UI thread.
        if (Interlocked.Exchange(ref _wifiCheckInProgress, 1) == 1)
        {
            return;
        }

        try
        {
            await CheckWifiFirmwareCoreAsync();
        }
        catch (OperationCanceledException)
        {
            // Expected when a firmware flash starts and cancels the probe.
        }
        catch (Exception ex)
        {
            // Fire-and-forget (_ = CheckWifiFirmwareAsync()): swallow so an unexpected probe failure
            // can't surface as an unobserved task exception. This is the unexpected path (per-device
            // failures are handled in the loop), so log at Error to capture it in telemetry/Sentry.
            _appLogger.Error(ex, "WiFi firmware probe failed unexpectedly.");
        }
        finally
        {
            Interlocked.Exchange(ref _wifiCheckInProgress, 0);
        }
    }

    private async Task CheckWifiFirmwareCoreAsync()
    {
        // WiFi firmware is an advanced, debug-gated feature: normal users shouldn't see or worry about
        // it. Gating the probe here means a non-debug session never queries the WINC at all — which also
        // means a blank/fresh WINC is never touched on connect for those users.
        if (!IsDebugModeEnabled)
        {
            return;
        }

        // Hard stop before touching any device: never probe while a flash is running. The probe sends
        // SCPI (POWer:STATe 1 / GETChipInfo?); a byte landing on a WINC that is bridging for a flash
        // corrupts the program and bricks the module. (The device only reconnects after it has rebooted
        // out of bridge mode, so a post-flash reconnect is already a safe moment to probe.)
        if (IsFirmwareUploading)
        {
            return;
        }

        // Fresh cancellation source for this probe pass; CancelWifiFirmwareCheck() (called when a
        // firmware flash starts) aborts the in-flight SCPI exchange so it can never overlap the flash.
        // Cancel (not just dispose) the prior source first.
        _wifiCheckCts?.Cancel();
        _wifiCheckCts?.Dispose();
        _wifiCheckCts = new CancellationTokenSource();
        var wifiCheckToken = _wifiCheckCts.Token;

        // Snapshot the UI-owned ConnectedDevices list on the dispatcher: this probe is fire-and-forget
        // and can resume on a thread-pool thread, while the list is mutated on the UI thread. One
        // snapshot is reused for both the prune below and the loop, so we never enumerate the live list.
        var dispatcher = Application.Current?.Dispatcher;
        var connectedDevices = dispatcher != null
            ? await dispatcher.InvokeAsync(() => ConnectionManager.Instance.ConnectedDevices.ToList())
            : ConnectionManager.Instance.ConnectedDevices.ToList();

        // Prune devices that have disconnected so a reconnect re-probes them. Case-insensitive to
        // match _wifiFirmwareCheckedDevices and serial handling elsewhere.
        var connectedKeys = connectedDevices
            .Select(GetWifiCheckKey)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _wifiFirmwareCheckedDevices.RemoveWhere(k => !connectedKeys.Contains(k));

        foreach (var device in connectedDevices)
        {
            // WiFi firmware can only be probed/flashed over USB on a serial device, and only on
            // the Nyquist family (WINC1500 module). ESP32-based / unrecognized devices integrate
            // WiFi into the SoC — GETChipInfo? returns non-version data — so skip them entirely.
            if (device.ConnectionType != Device.ConnectionType.Usb ||
                !device.HasWincWifiModule ||
                device is not SerialStreamingDevice serialStreamingDevice ||
                serialStreamingDevice is not ILanChipInfoProvider lanChipProvider)
            {
                continue;
            }

            // Only probe a device that is still fully connected (it may have dropped during the settle
            // wait), and never one that is mid firmware-update or the device currently being updated.
            // Re-checked per-device because these can flip during the loop's awaits.
            var deviceBeingUpdated = ConnectionManager.Instance.DeviceBeingUpdated;
            if (!device.IsConnected
                || IsFirmwareUploading
                || wifiCheckToken.IsCancellationRequested
                || (deviceBeingUpdated != null && ReferenceEquals(deviceBeingUpdated, device)))
            {
                continue;
            }

            var key = GetWifiCheckKey(device);
            // Add() returns false if already present, so the probe runs once per connection.
            // The synchronous Add before the first await guards against re-entrant UI refreshes.
            if (string.IsNullOrWhiteSpace(key) || !_wifiFirmwareCheckedDevices.Add(key))
            {
                continue;
            }

            try
            {
                _appLogger.Information($"Checking WiFi module firmware for {key}.");

                // SYSTem:POWer:STATe 1 — power on the WiFi module before GETChipInfo?.
                serialStreamingDevice.PowerOnWifiModule();

                var chipInfo = await TryGetLanChipInfoAsync(lanChipProvider, wifiCheckToken);
                var needsFlash = FirmwareUpdateCoordinator.WifiFirmwareNeedsFlash(chipInfo, out var reportedVersion);

                // These mutate UI-bound state (device properties + the NotificationList collection),
                // so marshal them onto the UI thread — the probe can resume on a thread-pool thread
                // when the ConnectedDevices change that started it fired off the UI thread.
                void ApplyResult()
                {
                    device.WifiFirmwareVersion = reportedVersion;
                    device.IsWifiFirmwareOutdated = needsFlash;
                    UpdateWifiFirmwareOnlyCommand.NotifyCanExecuteChanged();

                    if (needsFlash && !string.IsNullOrWhiteSpace(device.DeviceSerialNo))
                    {
                        AddWifiNotification(device, reportedVersion);
                    }
                    else if (!needsFlash)
                    {
                        RemoveWifiNotification(device);
                    }
                }

                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    // InvokeAsync (awaited) rather than a blocking Invoke — this probe runs on a
                    // thread-pool thread and shouldn't block it waiting on the UI thread. Pass the probe
                    // token so a pre-flash quiesce can interrupt this wait instead of blocking on the UI.
                    await dispatcher.InvokeAsync(ApplyResult, DispatcherPriority.Normal, wifiCheckToken);
                }
                else
                {
                    ApplyResult();
                }

                _appLogger.Information(needsFlash
                    ? $"WiFi firmware for {key} needs a flash (reported: {reportedVersion}, minimum: {FirmwareUpdateCoordinator.MinimumWifiFirmwareVersion})."
                    : $"WiFi firmware for {key} is up to date ({reportedVersion}).");
            }
            catch (OperationCanceledException)
            {
                // A firmware flash started and cancelled the probe — expected. Drop the key so the
                // device is re-probed once it reconnects after the flash.
                _appLogger.Information($"WiFi firmware check for {key} cancelled (firmware update in progress).");
                _wifiFirmwareCheckedDevices.Remove(key);
                return;
            }
            catch (Exception ex)
            {
                _appLogger.Warning($"WiFi firmware check failed for {key}: {ex.Message}");
                // Allow a retry on a later UI refresh.
                _wifiFirmwareCheckedDevices.Remove(key);
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
                _appLogger.Warning(ex,
                    $"WiFi chip info query attempt {attempt}/{WifiChipInfoMaxAttempts} failed.");
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

    /// <summary>
    /// Aborts any in-flight WiFi firmware probe. Called when a firmware flash begins so the
    /// probe's SCPI exchange can never overlap the flash and corrupt the bootloader handshake.
    /// </summary>
    private void CancelWifiFirmwareCheck()
    {
        try
        {
            _wifiCheckCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed — nothing in flight.
        }
    }

    private static string? GetWifiCheckKey(IStreamingDevice device)
    {
        if (!string.IsNullOrWhiteSpace(device.DeviceSerialNo))
        {
            return device.DeviceSerialNo;
        }

        return (device as SerialStreamingDevice)?.PortName;
    }

    [RelayCommand(CanExecute = nameof(CanUpdateWifiFirmwareOnly))]
    private async Task UpdateWifiFirmwareOnly()
    {
        if (IsFirmwareUploading)
        {
            return;
        }

        if (SelectedDevice?.ConnectionType != Device.ConnectionType.Usb ||
            SelectedDevice is not SerialStreamingDevice serialStreamingDevice)
        {
            return;
        }

        SelectedDeviceSupportsFirmwareUpdate = true;
        HasErrorOccured = false;
        IsUploadComplete = false;
        UploadWiFiProgress = 0;
        FirmwareUpdateStatusText = "Preparing WiFi firmware update...";

        ConnectionManager.Instance.DeviceBeingUpdated = SelectedDevice;

        _firmwareUploadCts?.Dispose();
        _firmwareUploadCts = new CancellationTokenSource();
        IsFirmwareUploading = true;
        CancelWifiFirmwareCheck();
        _appLogger.AddBreadcrumb("firmware", $"WiFi-only firmware update started for {serialStreamingDevice.Name}");

        try
        {
            var coreDevice = serialStreamingDevice.ConnectedCoreStreamingDevice;

            if (!coreDevice.IsConnected)
            {
                _appLogger.Error($"Device {serialStreamingDevice.Name} is not connected. Cannot update WiFi firmware on a disconnected device.");
                NotificationList.Add(new Notifications
                {
                    Message = $"Please connect device {serialStreamingDevice.Name} before attempting a WiFi firmware update.",
                    DeviceSerialNo = serialStreamingDevice.DeviceSerialNo
                });

                return;
            }

            await _firmwareCoordinator.UpdateWifiModuleAsync(coreDevice, serialStreamingDevice, _firmwareUploadCts.Token, force: true);

            IsUploadComplete = true;
            _appLogger.AddBreadcrumb("firmware", "WiFi firmware update completed");

            // Clear the outdated state and allow a fresh probe on reconnect.
            SelectedDevice.IsWifiFirmwareOutdated = false;
            RemoveWifiNotification(SelectedDevice);
            var key = GetWifiCheckKey(SelectedDevice);
            if (!string.IsNullOrWhiteSpace(key))
            {
                _wifiFirmwareCheckedDevices.Remove(key);
            }
            UpdateWifiFirmwareOnlyCommand.NotifyCanExecuteChanged();

            ShowUploadSuccessMessage("WiFi firmware");
        }
        catch (OperationCanceledException)
        {
            FirmwareUpdateStatusText = "WiFi firmware update canceled.";
            _appLogger.Warning("WiFi firmware update canceled by user.");
            _appLogger.AddBreadcrumb("firmware", "WiFi firmware update cancelled", Common.Loggers.BreadcrumbLevel.Warning);
        }
        catch (FirmwareUpdateException ex)
        {
            HasErrorOccured = true;
            _appLogger.Error(ex, $"WiFi firmware flash failed during '{ex.Operation}' ({ex.FailedState}).");
            _appLogger.AddBreadcrumb("firmware", $"WiFi firmware update failed: {ex.FailedState}", Common.Loggers.BreadcrumbLevel.Error);
            ShowWifiFlashFailedDialog();
        }
        catch (Exception ex)
        {
            HasErrorOccured = true;
            _appLogger.Error(ex, "Problem Uploading WiFi Firmware");
            _appLogger.AddBreadcrumb("firmware", "WiFi firmware update failed", Common.Loggers.BreadcrumbLevel.Error);
            ShowWifiFlashFailedDialog();
        }
        finally
        {
            IsFirmwareUploading = false;
            _firmwareUploadCts?.Dispose();
            _firmwareUploadCts = null;
            ConnectionManager.Instance.DeviceBeingUpdated = null;
        }
    }

    private bool CanUpdateWifiFirmwareOnly()
    {
        // Allowed on demand for any USB WINC1500 device — not gated on IsWifiFirmwareOutdated — so the
        // line can re-flash a module that reports as current (e.g. a prior flash that didn't take).
        // The command force-flashes regardless of the reported version.
        return !IsFirmwareUploading
            && SelectedDevice?.ConnectionType == Device.ConnectionType.Usb
            && SelectedDevice.HasWincWifiModule;
    }

    private void AddWifiNotification(IStreamingDevice device, string reportedVersion)
    {
        var versionText = string.Equals(reportedVersion, "Unknown", StringComparison.OrdinalIgnoreCase)
            ? "could not be read"
            : $"is out of date ({reportedVersion})";
        var message = $"Device {device.DeviceSerialNo}: WiFi module firmware {versionText} " +
                      $"(minimum {FirmwareUpdateCoordinator.MinimumWifiFirmwareVersion}). Click below to update just the WiFi module.";

        var existingNotification = NotificationList.FirstOrDefault(n => n.IsWifiFirmwareUpdate
                                                                        && !string.IsNullOrWhiteSpace(n.DeviceSerialNo)
                                                                        && string.Equals(n.DeviceSerialNo, device.DeviceSerialNo, StringComparison.OrdinalIgnoreCase));

        // Notifications.Message is init-only, so to refresh stale text (e.g. the reported WiFi
        // version changed on re-probe) replace the existing entry rather than mutating it.
        if (existingNotification != null)
        {
            if (existingNotification.Message == message)
            {
                return;
            }
            NotificationList.Remove(existingNotification);
        }

        NotificationList.Add(new Notifications
        {
            DeviceSerialNo = device.DeviceSerialNo,
            Message = message,
            IsWifiFirmwareUpdate = true
        });

        NotificationCount = NotificationList.Count;
    }

    private void RemoveWifiNotification(IStreamingDevice deviceToRemove)
    {
        if (deviceToRemove?.DeviceSerialNo == null)
        {
            return;
        }

        var notificationToRemove = NotificationList
            .FirstOrDefault(x => x.IsWifiFirmwareUpdate
                                 && !string.IsNullOrWhiteSpace(x.DeviceSerialNo)
                                 && string.Equals(x.DeviceSerialNo, deviceToRemove.DeviceSerialNo, StringComparison.OrdinalIgnoreCase));

        if (notificationToRemove != null)
        {
            NotificationList.Remove(notificationToRemove);
            NotificationCount = NotificationList.Count;
        }
    }

    private void ShowUploadSuccessMessage(string firmwareLabel)
    {
        void ShowDialog()
        {
            var successDialogViewModel =
                new SuccessDialogViewModel($"{firmwareLabel} update completed successfully.");
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

    /// <summary>
    /// User-facing message for a failed WiFi-module flash. A failed WINC bridge/program can leave
    /// the module in a confused state, so the reliable recovery is a full power-cycle before retrying
    /// (the cleanup path has already pulled the device out of transparent mode).
    /// </summary>
    private void ShowWifiFlashFailedDialog()
    {
        void ShowDialog()
        {
            var errorDialogViewModel = new ErrorDialogViewModel(
                "WiFi firmware flash failed. Disconnect the device, ensure power is cycled, and try again.");
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
            // WiFi firmware is debug-gated; now that it's visible, read the version for the
            // already-connected devices (the per-connection guard runs it at most once each).
            TriggerWifiFirmwareProbe();
        }
        else
        {
            _appLogger.Information("[DEBUG_MODE] Debug mode disabled");
            // Hide all WiFi-firmware concerns from the normal view: drop any "WiFi firmware out of
            // date" prompts and forget which devices were checked so re-enabling re-probes fresh.
            foreach (var device in ConnectedDevices.ToList())
            {
                RemoveWifiNotification(device);
            }
            _wifiFirmwareCheckedDevices.Clear();
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
