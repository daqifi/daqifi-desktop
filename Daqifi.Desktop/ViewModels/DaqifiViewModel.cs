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
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
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
    private bool _isProfileSettingsOpen;
    [ObservableProperty]
    private bool _isNotificationsOpen;
    [ObservableProperty]
    private bool _isFirmwareUpdatationFlyoutOpen;
    [ObservableProperty]
    private bool _isLogSummaryOpen;
    [ObservableProperty]
    private bool _isChannelSettingsOpen;
    [ObservableProperty]
    private bool _isLoggingSessionSettingsOpen;
    [ObservableProperty]
    private bool _isLiveGraphSettingsOpen;

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
    private IStreamingDevice _updateProfileSelectedDevice;
    [ObservableProperty]
    private IChannel _selectedChannel;
    [ObservableProperty]
    private Profile _selectedProfile;
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
    private readonly Func<string, string, IFirmwareUpdateService> _wifiFirmwareUpdateServiceFactory;
    private CancellationTokenSource? _firmwareUploadCts;
    private ConnectionDialogViewModel _connectionDialogViewModel;
    private string _selectedLoggingMode = "Stream to App";
    private bool _isLogToDeviceMode;
    private SdCardLogFormat _selectedSdCardLogFormat = SdCardLogFormat.Protobuf;
    private IStreamingDevice? _deviceBeingUpdated;
    private IDiskSpaceMonitor? _diskSpaceMonitor;
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

    public PlotLogger Plotter { get; private set; }
    public DatabaseLogger DbLogger { get; private set; }
    public SummaryLogger SummaryLogger { get; private set; }
    public ObservableCollection<IStreamingDevice> AvailableDevices { get; } = [];
    public ObservableCollection<IChannel> AvailableChannels { get; } = [];

    public IStreamingDevice UpdateProfileSelectedDevice
    {
        get => _updateProfileSelectedDevice;
        set
        {
            _updateProfileSelectedDevice = value;
            GetAvailableChannels(_updateProfileSelectedDevice);
            OnPropertyChanged();
        }
    }

    public bool IsLogging
    {
        get => _isLogging;
        set
        {
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
                _diskSpaceMonitor?.StartMonitoring();

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
        }
    }

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
            OnPropertyChanged();
        }
    }

    public int SelectedStreamingFrequency
    {
        get => _selectedStreamingFrequency;
        set
        {
            if (value < 1) { return; }

            if (LoggingManager.Instance.Active)
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
    public ICommand DeleteAllLoggingSessionCommand { get; private set; }
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
        App.ServiceProvider?.GetService<ILogger<FirmwareUpdateService>>())
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
        Func<string, string, IFirmwareUpdateService>? wifiFirmwareUpdateServiceFactory = null)
    {
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _firmwareDownloadService = firmwareDownloadService ?? CreateDefaultFirmwareDownloadService();
        var resolvedLogger = firmwareLogger ?? NullLogger<FirmwareUpdateService>.Instance;
        _firmwareUpdateService = firmwareUpdateService ?? CreateDefaultFirmwareUpdateService(_firmwareDownloadService, resolvedLogger);
        _wifiFirmwareUpdateServiceFactory = wifiFirmwareUpdateServiceFactory ?? CreateWifiFirmwareUpdateService;

        var app = Application.Current as App;
        if (app != null)
        {
            if (app.IsWindowInit)
            {
                try
                {
                    var loggingContext = App.ServiceProvider.GetRequiredService<IDbContextFactory<LoggingContext>>();
                    RegisterCommands();

                    // Manage connected streamingDevice list
                    ConnectionManager.Instance.PropertyChanged += UpdateUi;

                    // Manage data for plotting
                    LoggingManager.Instance.PropertyChanged += UpdateUi;
                    Plotter = new PlotLogger();
                    LoggingManager.Instance.AddLogger(Plotter);

                    // Database logging
                    DbLogger = new DatabaseLogger(loggingContext);
                    LoggingManager.Instance.AddLogger(DbLogger);

                    // Device Logs View Model
                    DeviceLogsViewModel = new DeviceLogsViewModel();

                    //Xml profiles load
                    LoggingManager.Instance.AddAndRemoveProfileXml(null, false);
                    _ = new ObservableCollection<Profile>(LoggingManager.Instance.LoadProfilesFromXml());

                    // Notifications
                    _versionNotification = new VersionNotification();
                    _ = LoggingManager.Instance.CheckApplicationVersion(_versionNotification);

                    GetUpdateProfileAvailableDevice();

                    // Summary Logger
                    SummaryLogger = new SummaryLogger();
                    LoggingManager.Instance.AddLogger(SummaryLogger);

                    // Disk space monitoring
                    _diskSpaceMonitor = new DiskSpaceMonitor(App.DaqifiDataDirectory);
                    _diskSpaceMonitor.LowSpaceWarning += OnDiskSpaceLowWarning;
                    _diskSpaceMonitor.CriticalSpaceReached += OnDiskSpaceCritical;

                    if (LoggingManager.Instance.LoggingSessions == null || !LoggingManager.Instance.LoggingSessions.Any())
                    {
                        LoggingManager.Instance.LoggingSessions = LoggingManager.Instance.LoadPersistedLoggingSessions();
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
            ShowUploadSuccessMessage();
        }
        catch (OperationCanceledException)
        {
            FirmwareUpdateStatusText = "Firmware update canceled.";
            _appLogger.Warning("Firmware update canceled by user.");
        }
        catch (FirmwareUpdateException ex)
        {
            HandleFirmwareUpdateException(ex);
        }
        catch (Exception ex)
        {
            HasErrorOccured = true;
            _appLogger.Error(ex, "Problem Uploading Firmware");
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
    private void ShowAddChannelDialog()
    {
        var addChannelDialogViewModel = new AddChannelDialogViewModel();
        _dialogService.ShowDialog<AddChannelDialog>(this, addChannelDialogViewModel);
    }

    [RelayCommand]
    private void ShowSelectColorDialog()
    {
        IColorable? item = SelectedChannel;
        if (item == null)
        {
            return;
        }

        var selectColorDialogViewModel = new SelectColorDialogViewModel(item);
        _dialogService.ShowDialog<SelectColorDialog>(this, selectColorDialogViewModel);
    }

    [RelayCommand]
    private void ShowDAQiFiSettingsDialog()
    {
        var settingsViewModel = new SettingsViewModel();
        _dialogService.ShowDialog<SettingsDialog>(this, settingsViewModel);
    }

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
        await SelectedDevice.UpdateNetworkConfiguration();
        _dialogService.ShowDialog<SuccessDialog>(this, new SuccessDialogViewModel("WiFi settings updated."));
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
    private void OpenDeviceSettings(IStreamingDevice? device)
    {
        if (device == null)
        {
            return;
        }

        SelectedDeviceSupportsFirmwareUpdate = device.ConnectionType == Device.ConnectionType.Usb;

        CloseFlyouts();
        SelectedDevice = device;
        IsDeviceSettingsOpen = true;
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
    private void OpenChannelSettings(IChannel? channel)
    {
        if (channel == null)
        {
            return;
        }

        SelectedChannel = channel;
        CloseFlyouts();
        IsChannelSettingsOpen = true;
    }

    [RelayCommand]
    private void OpenLogSummary()
    {
        CloseFlyouts();
        IsLogSummaryOpen = true;
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
            finally
            {
                IsLoggedDataBusy = false;
            }
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

            var result = await ShowMessage("Delete Confirmation", $"Are you sure you want to delete {SelectedLoggingSession.Name}?", MessageDialogStyle.AffirmativeAndNegative).ConfigureAwait(false);
            if (result != MessageDialogResult.Affirmative)
            {
                return;
            }

            IsLoggedDataBusy = true;
            LoggedDataBusyReason = $"Deleting Logging Session #{SelectedLoggingSession.ID}";
            var bw = new BackgroundWorker();
            bw.DoWork += delegate
            {
                var deleteSucceeded = false;
                var sessionToDelete = SelectedLoggingSession;
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
                    });
                }
            };

            bw.RunWorkerCompleted += (s, e) =>
            {
                IsLoggedDataBusy = false;
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
            if (LoggingManager.Instance.LoggingSessions.Count == 0)
            {
                return;
            }

            if (LoggingManager.Instance.Active)
            {
                await ShowMessage(
                    "Cannot Delete",
                    "Please stop logging before deleting all sessions.",
                    MessageDialogStyle.Affirmative).ConfigureAwait(false);
                return;
            }

            var result = await ShowMessage(
                "Delete Confirmation",
                "Are you sure you want to delete all logging sessions? This cannot be undone.",
                MessageDialogStyle.AffirmativeAndNegative).ConfigureAwait(false);
            if (result != MessageDialogResult.Affirmative)
            {
                return;
            }

            IsLoggedDataBusy = true;
            LoggedDataBusyReason = "Deleting All Logging Sessions";
            var bw = new BackgroundWorker();
            bw.DoWork += delegate
            {
                // Stop the consumer thread so it releases all DB connections
                DbLogger.SuspendConsumer();
                try
                {
                    DbLogger.ClearBuffer();

                    // Release all pooled SQLite connections so the file is not locked
                    SqliteConnection.ClearAllPools();

                    var dbPath = App.DatabasePath;
                    DeleteFileIfExists(dbPath);
                    DeleteFileIfExists(dbPath + "-wal");
                    DeleteFileIfExists(dbPath + "-shm");

                    // Recreate the database schema by creating a fresh context
                    using var context = App.ServiceProvider
                        .GetRequiredService<IDbContextFactory<LoggingContext>>()
                        .CreateDbContext();

                    Application.Current.Dispatcher.Invoke(delegate
                    {
                        LoggingManager.Instance.LoggingSessions.Clear();
                        DbLogger.ClearPlot();
                    });
                }
                catch (IOException ioEx)
                {
                    _appLogger.Error(ioEx, "Database file is in use. Please try again.");
                }
                catch (Exception ex)
                {
                    _appLogger.Error(ex, "Failed to delete all logging sessions.");
                }
                finally
                {
                    DbLogger.ResumeConsumer();
                }
            };

            bw.RunWorkerCompleted += (s, e) =>
            {
                IsLoggedDataBusy = false;
            };

            bw.RunWorkerAsync();
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Error initiating deletion of all logging sessions");
        }
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

    [RelayCommand]
    public void ShowAddProfileDialog()
    {
        try
        {
            if (!EnsureAnyDeviceConnected()) return;

            var addProfileDialogViewModel = new AddProfileDialogViewModel();
            _dialogService.ShowDialog<AddprofileDialog>(this, addProfileDialogViewModel);
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Error opening add profile dialog");
        }
    }

    [RelayCommand]
    private void ShowAddProfileConfirmation()
    {
        try
        {
            if (!EnsureAnyDeviceConnected()) return;

            LoggingManager.Instance.AddAndRemoveProfileXml(null, false);
            var addProfileConfirmationDialogViewModel = new AddProfileConfirmationDialogViewModel();
            _dialogService.ShowDialog<AddProfileConfirmationDialog>(this, addProfileConfirmationDialogViewModel);
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Error opening confirmation dialog");
        }
    }

    [RelayCommand]
    public void GetUpdateProfileAvailableDevice()
    {
        foreach (var device in ConnectionManager.Instance.ConnectedDevices)
        {
            AvailableDevices.Add(device);
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
    private void RemoveProfile(Profile? profile)
    {
        if (profile == null)
        {
            return;
        }
        if (LoggingManager.Instance.Active)
        {
            var errorDialogViewModel = new ErrorDialogViewModel("Cannot remove profile while logging");
            _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
            return;
        }
        if (profile.IsProfileActive)
        {
            var errorDialogViewModel = new ErrorDialogViewModel("Cannot remove profile while profile is active");
            _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
            return;
        }
        if (Profiles.Any(x => x.IsProfileActive))
        {
            var errorDialogViewModel = new ErrorDialogViewModel("Cannot remove profile while profile is active");
            _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
            return;
        }
        LoggingManager.Instance.UnsubscribeProfile(profile);
        ActiveChannels.Clear();
        ActiveInputChannels.Clear();
        Profiles.Remove(profile);
    }

    [RelayCommand]
    private void OpenNotifications()
    {
        IsNotificationsOpen = true;
    }

    [RelayCommand]
    private void OpenProfileSettings()
    {
        if (SelectedProfile == null)
        {
            _appLogger.Error("Error opening channel settings");
            return;
        }
        CloseFlyouts();
        if (LoggingManager.Instance.Active)
        {
            var errorDialogViewModel = new ErrorDialogViewModel("Cannot edit profile while logging");
            _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
            return;
        }
        if (SelectedProfile.IsProfileActive)
        {
            var errorDialogViewModel = new ErrorDialogViewModel("Cannot edit profile while profile is active");
            _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
            return;
        }

        LoggingManager.Instance.SelectedProfile = SelectedProfile;
        LoggingManager.Instance.Flag = false;
        LoggingManager.Instance.SelectedProfileChannels.Clear();
        LoggingManager.Instance.SelectedProfileDevices.Clear();

        // Build profile-to-device mapping using two-pass strategy
        var claimedDevices = new HashSet<IStreamingDevice>();
        var profileToDevice = new Dictionary<ProfileDevice, IStreamingDevice>();

        // Pass 1: Prefer exact serial number match
        foreach (var profileDev in SelectedProfile.Devices)
        {
            var exactMatch = ConnectionManager.Instance.ConnectedDevices.FirstOrDefault(cd =>
                !string.IsNullOrEmpty(profileDev.DeviceSerialNo) &&
                string.Equals(cd.DeviceSerialNo, profileDev.DeviceSerialNo, StringComparison.OrdinalIgnoreCase) &&
                !claimedDevices.Contains(cd));

            if (exactMatch != null)
            {
                profileToDevice[profileDev] = exactMatch;
                claimedDevices.Add(exactMatch);
            }
        }

        // Pass 2: Fall back to model match for unmatched profile devices
        foreach (var profileDev in SelectedProfile.Devices)
        {
            if (profileToDevice.ContainsKey(profileDev))
            {
                continue;
            }

            var modelMatch = ConnectionManager.Instance.ConnectedDevices.FirstOrDefault(cd =>
                cd.DevicePartNumber == profileDev.DevicePartName &&
                !claimedDevices.Contains(cd));

            if (modelMatch != null)
            {
                profileToDevice[profileDev] = modelMatch;
                claimedDevices.Add(modelMatch);
            }
        }

        foreach (var selectedDevice in SelectedProfile.Devices)
        {
            LoggingManager.Instance.SelectedProfileDevices.Add(selectedDevice);
            if (selectedDevice.Channels != null && selectedDevice.Channels.Count > 0)
            {
                foreach (var selectedchannel in selectedDevice.Channels)
                {
                    if (selectedchannel.IsChannelActive)
                    {
                        selectedchannel.SerialNo = selectedDevice.DeviceSerialNo;
                        LoggingManager.Instance.SelectedProfileChannels.Add(selectedchannel);
                    }
                }
            }

            // Use the matched connected device from two-pass mapping
            if (profileToDevice.TryGetValue(selectedDevice, out var device))
            {
                foreach (var channels in device.DataChannels)
                {
                    // Check if the channel is already in the selected profile channels
                    var profileChannel = LoggingManager.Instance.SelectedProfileChannels
                        .FirstOrDefault(x => x.Name == channels.Name && x.SerialNo == selectedDevice.DeviceSerialNo);

                    if (profileChannel == null)
                    {
                        profileChannel = new ProfileChannel
                        {
                            Name = channels.Name,
                            SerialNo = selectedDevice.DeviceSerialNo,
                            Type = channels.TypeString,
                            IsChannelActive = false,
                        };
                        LoggingManager.Instance.SelectedProfileChannels.Add(profileChannel);
                    }
                }
            }

            var deviceSelected = LoggingManager.Instance.SelectedProfileDevices.FirstOrDefault(x => x.DeviceSerialNo == selectedDevice.DeviceSerialNo);
            if (deviceSelected?.Channels is { Count: > 0 })
            {
                deviceSelected.Channels.Clear();
                deviceSelected.Channels = LoggingManager.Instance.SelectedProfileChannels.Where(x => x.SerialNo == selectedDevice.DeviceSerialNo).ToList();
            }
        }
        LoggingManager.Instance.callPropertyChange();
        IsProfileSettingsOpen = true;
    }

    [RelayCommand]
    public void GetAvailableChannels(IStreamingDevice device)
    {
        AvailableChannels.Clear();

        // Sort channels naturally by name before adding to collection
        var sortedChannels = device.DataChannels.NaturalOrderBy(channel => channel.Name);

        foreach (var channel in sortedChannels)
        {
            AvailableChannels.Add(channel);
        }
    }

    [RelayCommand]
    public void SaveExistingSetting()
    {
        try
        {
            if (!EnsureAnyDeviceConnected()) return;

            if (ConnectionManager.Instance.ConnectedDevices.Count > 2)
            {
                var errorDialogViewModel = new ErrorDialogViewModel("Cannot add profile with  connected devices more than two");
                _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
                return;
            }
            var addProfileModel = new AddProfileModel
            {
                ProfileList = []
            };

            var createdDate = DateTime.Now;
            var newProfile = new Profile
            {

                Name = "DAQiFi Profile " + createdDate,
                ProfileId = Guid.NewGuid(),
                CreatedOn = createdDate,
                Devices = []
            };

            foreach (var selectedDevice in ConnectedDevices)
            {
                if (selectedDevice?.DataChannels?.Count > 0)
                {
                    var device = new ProfileDevice
                    {
                        MacAddress = selectedDevice.MacAddress,
                        DeviceName = selectedDevice.Name,
                        DevicePartName = selectedDevice.DevicePartNumber,
                        DeviceSerialNo = selectedDevice.DeviceSerialNo,
                        SamplingFrequency = selectedDevice.StreamingFrequency,
                        Channels = new List<ProfileChannel>()
                    };

                    foreach (var dataChannel in selectedDevice.DataChannels)
                    {
                        if (dataChannel.IsActive)
                        {
                            var profileChannel = new ProfileChannel
                            {
                                Name = dataChannel.Name,
                                Type = dataChannel.TypeString,
                                IsChannelActive = true,
                                SerialNo = dataChannel.DeviceSerialNo
                            };
                            // Add the active channel to the current device
                            device.Channels.Add(profileChannel);
                        }
                    }
                    // Add the device to the profile
                    newProfile.Devices.Add(device);
                }
            }
            addProfileModel.ProfileList.Add(newProfile);
            LoggingManager.Instance.SubscribeProfile(newProfile);
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Error saving existing settings");
        }
    }

    [RelayCommand]
    private void ActivateProfile(Profile? profile)
    {
        try
        {
            if (profile == null)
            {
                return;
            }

            SelectedProfile = profile;

            // Check for multiple active profiles
            var anyActiveProfile = Profiles.FirstOrDefault(x => x.IsProfileActive);
            if (anyActiveProfile != null && anyActiveProfile.ProfileId != SelectedProfile.ProfileId)
            {
                var errorDialogViewModel = new ErrorDialogViewModel("Multiple Profiles Cannot be Active.");
                _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
                _appLogger.Error("Multiple Profiles Cannot be Active.");
                return;
            }

            // Build profile-to-device mapping using two-pass strategy:
            // Pass 1: exact serial number match
            // Pass 2: fall back to model match for unmatched profile devices
            var claimedDevices = new HashSet<IStreamingDevice>();
            var profileToDevice = new Dictionary<ProfileDevice, IStreamingDevice>();

            // Pass 1: Prefer exact serial number match
            foreach (var profileDevice in SelectedProfile.Devices)
            {
                var exactMatch = ConnectedDevices.FirstOrDefault(cd =>
                    !string.IsNullOrEmpty(profileDevice.DeviceSerialNo) &&
                    string.Equals(cd.DeviceSerialNo, profileDevice.DeviceSerialNo, StringComparison.OrdinalIgnoreCase) &&
                    !claimedDevices.Contains(cd));

                if (exactMatch != null)
                {
                    profileToDevice[profileDevice] = exactMatch;
                    claimedDevices.Add(exactMatch);
                }
            }

            // Pass 2: Fall back to model match for unmatched profile devices
            foreach (var profileDevice in SelectedProfile.Devices)
            {
                if (profileToDevice.ContainsKey(profileDevice))
                {
                    continue;
                }

                var modelMatch = ConnectedDevices.FirstOrDefault(cd =>
                    cd.DevicePartNumber == profileDevice.DevicePartName &&
                    !claimedDevices.Contains(cd));

                if (modelMatch != null)
                {
                    profileToDevice[profileDevice] = modelMatch;
                    claimedDevices.Add(modelMatch);
                }
            }

            // Block only if no devices were matched
            if (profileToDevice.Count == 0)
            {
                var errorDialogViewModel = new ErrorDialogViewModel("Profile cannot be active. No connected devices with matching device model.");
                _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
                return;
            }

            // Warn if some profile devices could not be matched (count-aware)
            var unmatchedProfileDevices = SelectedProfile.Devices
                .Where(pd => !profileToDevice.ContainsKey(pd))
                .ToList();
            if (unmatchedProfileDevices.Count > 0)
            {
                var warningMsg = $"Warning: The following devices from the profile are not currently connected: {string.Join(", ", unmatchedProfileDevices.Select(d => $"{d.DeviceName} (S/N: {d.DeviceSerialNo})"))}. Profile will be loaded with available devices.";
                var warningDialogViewModel = new ErrorDialogViewModel(warningMsg);
                _dialogService.ShowDialog<ErrorDialog>(this, warningDialogViewModel);
            }

            // Check if logging is active
            if (LoggingManager.Instance.Active)
            {
                var errorDialogViewModel = new ErrorDialogViewModel("Profile cannot be active while logging.");
                _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
                return;
            }

            // Iterate through matched profile-device pairs
            foreach (var (profileDevice, connectedDevice) in profileToDevice)
            {
                SelectedDevice = connectedDevice;
                UpdateProfileSelectedDevice = SelectedDevice;

                // Update device frequencies
                SelectedStreamingFrequency = profileDevice.SamplingFrequency;
                connectedDevice.StreamingFrequency = profileDevice.SamplingFrequency;

                // Collect matching channels for this specific profile device
                var channelsToActivate = new List<IChannel>();
                foreach (var channel in profileDevice.Channels)
                {
                    if (!channel.IsChannelActive)
                    {
                        continue;
                    }

                    // Match channel by name, type, and the connected device's model
                    var matchedChannel = AvailableChannels
                        .FirstOrDefault(x => x.Name == channel.Name.Trim() &&
                                             x.TypeString == channel.Type.Trim() &&
                                             x.DeviceName == connectedDevice.DevicePartNumber);

                    if (matchedChannel != null)
                    {
                        channelsToActivate.Add(matchedChannel);
                    }
                }

                if (SelectedProfile.IsProfileActive)
                {
                    connectedDevice.RemoveAllChannels();
                    foreach (var ch in channelsToActivate)
                    {
                        LoggingManager.Instance.Unsubscribe(ch);
                    }
                }
                else
                {
                    connectedDevice.AddChannels(channelsToActivate);
                    foreach (var ch in channelsToActivate)
                    {
                        LoggingManager.Instance.Subscribe(ch);
                    }
                }
            }
            // Toggle profile's active state
            SelectedProfile.IsProfileActive = !SelectedProfile.IsProfileActive;
        }
        catch (Exception ex)
        {
            _appLogger.Error("Error activating Profile: " + ex.Message);
        }
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

                GetUpdateProfileAvailableDevice();
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
    public void CloseFlyouts()
    {
        IsProfileSettingsOpen = false;
        IsDeviceSettingsOpen = false;
        IsChannelSettingsOpen = false;
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
        Application.Current?.Dispatcher?.Invoke(() =>
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
        Application.Current?.Dispatcher?.Invoke(() =>
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
