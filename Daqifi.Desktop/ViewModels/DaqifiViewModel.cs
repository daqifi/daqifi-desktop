﻿using Daqifi.Desktop.Bootloader;
using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Commands;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Configuration;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Device.HidDevice;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.Loggers;
using Daqifi.Desktop.Models;
using Daqifi.Desktop.UpdateVersion;
using Daqifi.Desktop.View;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Daqifi.Desktop.Device.SerialDevice;
using Application = System.Windows.Application;
using File = System.IO.File;
using Daqifi.Desktop.DataModel.Network;

namespace Daqifi.Desktop.ViewModels;

public class DaqifiViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private readonly AppLogger _appLogger = AppLogger.Instance;
        
    #region Private Variables
    private bool _isBusy;
    private bool _isLoggedDataBusy;
    private bool _isDeviceSettingsOpen;
    private bool _isProfileSettingsOpen;
    private bool _isNotificationsOpen;
    private bool _isFirmwareUpdatationFlyoutOpen;
    private bool _isLogSummaryOpen;
    private bool _isChannelSettingsOpen;
    private bool _isLoggingSessionSettingsOpen;
    private bool _isLiveGraphSettingsOpen;
    private int _width = 800;
    private int _height = 600;
    private int _sidePanelWidth = 85;
    private int _topToolbarHeight = 30;
    private int _selectedIndex;
    private int _selectedStreamingFrequency;
    public WindowState _viewWindowState;
    private readonly IDialogService _dialogService;
    private IStreamingDevice _selectedDevice;
    private VersionNotification? _versionNotification;
    private IStreamingDevice _updateProfileSelectedDevice;
    private IChannel _selectedChannel;
    private Profile _selectedProfile;
    private LoggingSession _selectedLoggingSession;
    private bool _isLogging;
    private bool _canToggleLogging;
    private string _loggedDataBusyReason;
    private string _firmwareFilePath;
    private Pic32Bootloader _bootloader;
    private string _version;
    private bool _isFirmwareUploading;
    private bool _isUploadComplete;
    private bool _hasErrorOccured;
    private int _uploadFirmwareProgress;
    private int _uploadWiFiProgress;
    private HidDeviceFinder _hidDeviceFinder;
    private bool _hasNoHidDevices = true;
    private ConnectionDialogViewModel _connectionDialogViewModel;
    private readonly IDbContextFactory<LoggingContext> _loggingContext;
    private string _selectedLoggingMode = "Stream to App";
    private bool _isLogToDeviceMode;
    #endregion

    #region Properties

    public ObservableCollection<HidFirmwareDevice> AvailableHidDevices { get; } = [];
    public ObservableCollection<IStreamingDevice> ConnectedDevices { get; } = [];
    public ObservableCollection<Profile> profiles { get; } = [];

    public ObservableCollection<Notifications> notificationlist { get; } = [];
    public ObservableCollection<IChannel> ActiveChannels { get; } = [];
    public ObservableCollection<IChannel> ActiveInputChannels { get; } = [];
    public ObservableCollection<LoggingSession> LoggingSessions { get; } = [];

    public PlotLogger Plotter { get; private set; }
    public DatabaseLogger DbLogger { get; private set; }
    public SummaryLogger SummaryLogger { get; private set; }
    public ObservableCollection<IStreamingDevice> AvailableDevices { get; } = [];
    public ObservableCollection<IChannel> AvailableChannels { get; } = [];
    public string Version
    {
        get => _version;
        set
        {
            _version = value;
            OnPropertyChanged();
        }
    }

    public string FirmwareFilePath
    {
        get => _firmwareFilePath;
        set
        {
            _firmwareFilePath = value;
            OnPropertyChanged();
        }
    }

    public bool IsFirmwareUploading
    {
        get => _isFirmwareUploading;
        set
        {
            _isFirmwareUploading = value;
            OnPropertyChanged();
        }
    }

    public bool IsUploadComplete
    {
        get => _isUploadComplete;
        set
        {
            _isUploadComplete = value;
            OnPropertyChanged();
        }
    }

    public bool HasErrorOccured
    {
        get => _hasErrorOccured;
        set
        {
            _hasErrorOccured = value;
            OnPropertyChanged();
        }
    }

    public int UploadFirmwareProgress
    {
        get => _uploadFirmwareProgress;
        set
        {
            _uploadFirmwareProgress = value;
            OnPropertyChanged();
            OnPropertyChanged("UploadFirmwareProgressText");
        }
    }
    public int UploadWiFiProgress
    {
        get => _uploadWiFiProgress;
        set
        {
            _uploadWiFiProgress = value;
            OnPropertyChanged();
        }
    }

    public string UploadFirmwareProgressText => ($"Upload Progress: {UploadFirmwareProgress}%");
    public bool HasNoHidDevices
    {
        get => _hasNoHidDevices;
        set
        {
            _hasNoHidDevices = value;
            OnPropertyChanged();
        }
    }
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
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            _isBusy = value;
            OnPropertyChanged();
        }
    }

    public bool IsLoggedDataBusy
    {
        get => _isLoggedDataBusy;
        private set
        {
            _isLoggedDataBusy = value;
            OnPropertyChanged();
        }
    }

    public bool IsLogging
    {
        get => _isLogging;
        set
        {
            _isLogging = value;
            if (_isLogging)
            {
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
            LoggingManager.Instance.Active = value;
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

    public bool IsDeviceSettingsOpen
    {
        get => _isDeviceSettingsOpen;
        set
        {
            _isDeviceSettingsOpen = value;
            OnPropertyChanged();
        }
    }
    public bool IsProfileSettingsOpen
    {
        get => _isProfileSettingsOpen;
        set
        {
            _isProfileSettingsOpen = value;
            OnPropertyChanged();
        }
    }
    public bool IsNotificationsOpen
    {
        get => _isNotificationsOpen;
        set
        {
            _isNotificationsOpen = value;
            OnPropertyChanged();
        }
    }

    public bool IsFirmwareUpdatationFlyoutOpen
    {
        get => _isFirmwareUpdatationFlyoutOpen;
        set
        {
            _isFirmwareUpdatationFlyoutOpen = value;
            OnPropertyChanged();
        }
    }

    public bool IsLoggingSessionSettingsOpen
    {
        get => _isLoggingSessionSettingsOpen;
        set
        {
            _isLoggingSessionSettingsOpen = value;
            OnPropertyChanged();
        }
    }

    public bool IsLogSummaryOpen
    {
        get => _isLogSummaryOpen;
        set
        {
            _isLogSummaryOpen = value;
            OnPropertyChanged();
        }
    }

    public bool IsChannelSettingsOpen
    {
        get => _isChannelSettingsOpen;
        set
        {
            _isChannelSettingsOpen = value;
            OnPropertyChanged();
        }
    }

    public bool IsNotLogging => !IsLogging;

    private int _notificationCount;

    public int NotificationCount
    {
        get => _notificationCount;
        set
        {
            _notificationCount = value;
            OnPropertyChanged();
        }
    }

    private string _versionName;
    public string VersionName
    {
        get => _versionName;
        set
        {
            _versionName = value;
            OnPropertyChanged();
        }
    }

    public bool IsLiveGraphSettingsOpen
    {
        get => _isLiveGraphSettingsOpen;
        set
        {
            _isLiveGraphSettingsOpen = value;
            OnPropertyChanged();
        }
    }

    public int Width
    {
        get => _width;
        set
        {
            _width = value;
            OnPropertyChanged();
            OnPropertyChanged("FlyoutWidth");
        }
    }

    public int Height
    {
        get => _height;
        set
        {
            _height = value;
            OnPropertyChanged();
            OnPropertyChanged("FlyoutHeight");
        }
    }

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
                return SystemParameters.WorkArea.Width - _sidePanelWidth;
            }
            return _width - _sidePanelWidth;
        }
    }

    public double FlyoutHeight
    {
        get
        {
            if (ViewWindowState == WindowState.Maximized)
            {
                return SystemParameters.WorkArea.Height - _topToolbarHeight;
            }
            return _height - _topToolbarHeight;
        }
    }

    public IStreamingDevice SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            _selectedDevice = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSelectedDeviceUsb));
        }
    }

    public IChannel SelectedChannel
    {
        get => _selectedChannel;
        set
        {
            _selectedChannel = value;
            OnPropertyChanged();
        }
    }
    public Profile SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            _selectedProfile = value;
            OnPropertyChanged();

        }
    }

    public LoggingSession SelectedLoggingSession
    {
        get => _selectedLoggingSession;
        set
        {
            _selectedLoggingSession = value;
            OnPropertyChanged();
        }
    }
        
    public string LoggedDataBusyReason
    {
        get => _loggedDataBusyReason;
        set
        {
            _loggedDataBusyReason = value;
            OnPropertyChanged();
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
    private string _loggedSessionName;
    public string LoggedSessionName
    {
        get => _loggedSessionName;
        set
        {
            if (_loggedSessionName == value) { return; }

            _loggedSessionName = value;
            OnPropertyChanged();
        }
    }

    public string SelectedLoggingMode
    {
        get => _selectedLoggingMode;
        set
        {
            if (_selectedLoggingMode != value)
            {
                _selectedLoggingMode = value;
                    
                // Handle ComboBoxItem content
                string mode = value;
                if (value?.Contains("ComboBoxItem") == true)
                {
                    mode = value.Split(':').Last().Trim();
                }
                    
                IsLogToDeviceMode = mode == "Log to Device";
                var deviceMode = IsLogToDeviceMode ? DeviceMode.LogToDevice : DeviceMode.StreamToApp;
                    
                // Switch mode on all devices
                foreach (var device in ConnectedDevices)
                {
                    device.SwitchMode(deviceMode);
                }
                    
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

    public DeviceLogsViewModel DeviceLogsViewModel { get; private set; }

    public bool IsSelectedDeviceUsb => SelectedDevice is SerialStreamingDevice;
    #endregion

    #region Constructor
    public DaqifiViewModel() : this(ServiceLocator.Resolve<IDialogService>()) { }
    public DaqifiViewModel(IDialogService dialogService)
    {
        var app = Application.Current as App;
        if (app != null)
        {
            if (app.IsWindowInit)
            {
                try
                {
                    _dialogService = dialogService;
                    _loggingContext = App.ServiceProvider.GetRequiredService<IDbContextFactory<LoggingContext>>();
                    RegisterCommands();

                    // Manage connected streamingDevice list
                    ConnectionManager.Instance.PropertyChanged += UpdateUi;

                    // Manage data for plotting
                    LoggingManager.Instance.PropertyChanged += UpdateUi;
                    Plotter = new PlotLogger();
                    LoggingManager.Instance.AddLogger(Plotter);

                    // Database logging
                    DbLogger = new DatabaseLogger(_loggingContext);
                    LoggingManager.Instance.AddLogger(DbLogger);

                    // Device Logs View Model
                    DeviceLogsViewModel = new DeviceLogsViewModel();

                    //Xml profiles load
                    LoggingManager.Instance.AddAndRemoveProfileXml(null, false);
                    ObservableCollection<Daqifi.Desktop.Models.Profile> observableProfileList = new ObservableCollection<Daqifi.Desktop.Models.Profile>(LoggingManager.Instance.LoadProfilesFromXml());
                    //  Notifications 

                    _versionNotification = new VersionNotification();
                    _ = LoggingManager.Instance.CheckApplicationVersion(_versionNotification);

                    GetUpdateProfileAvailableDevice();

                    // Summary Logger
                    SummaryLogger = new SummaryLogger();
                    LoggingManager.Instance.AddLogger(SummaryLogger);

                    using (var context = _loggingContext.CreateDbContext())
                    {
                        var savedLoggingSessions = new List<LoggingSession>();
                        var previousSampleSessions = (from s in context.Sessions select s).ToList();
                        foreach (var session in previousSampleSessions)
                        {
                            if (!savedLoggingSessions.Contains(session)) { savedLoggingSessions.Add(session); }
                        }
                        LoggingManager.Instance.LoggingSessions = savedLoggingSessions;
                    }

                    //Configure Default Grid Lines
                    Plotter.ShowingMinorXAxisGrid = false;
                    Plotter.ShowingMinorYAxisGrid = false;

                    FirewallConfiguration.InitializeFirewallRules();

                }
                catch (Exception ex)
                {
                    _appLogger.Error(ex, "DAQifiViewModel");
                }
            }
            app.IsWindowInit = true;
        }
    }

    private void OnHidDevicePropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "Version")
        {
            Version = _bootloader.Version;
        }
    }

    #endregion

    #region Command Properties
    public ICommand UploadFirmwareCommand { get; set; }
    private bool CanUploadFirmware(object o)
    {
        return true;
    }

    public ICommand ShowAddProfileDialogCommand { get; private set; }
    private bool CanShowAddProfileDialog(object o)
    {
        return true;
    }
    public ICommand ShowAddProfileConfirmationDialogCommand { get; private set; }
    private bool CanShowAddProfileConfirmationDialogCommand(object o)
    {
        return true;
    }
    public ICommand ShowConnectionDialogCommand { get; private set; }
    private bool CanShowConnectionDialog(object o)
    {
        return true;
    }

    public ICommand ShowAddChannelDialogCommand { get; private set; }
    private bool CanShowAddChannelDialog(object o)
    {
        if (LoggingManager.Instance.Active)
        {
            var errorDialogViewModel = new ErrorDialogViewModel("Cannot add channel while logging.");
            _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);

            return false;
        }
        return true;
    }

    public ICommand ShowSelectColorDialogCommand { get; private set; }
    private bool CanShowSelectColorDialogCommand(object o)
    {
        return true;
    }

    public ICommand ShowDAQifiSettingsDialogCommand { get; private set; }
    private bool CanShowDAQifiSettingsDialog(object o)
    {
        return true;
    }
    public ICommand RemoveProfileCommand { get; private set; }
    private bool CanRemoveProfileCommand(object o)
    {
        return true;
    }

    public ICommand RemoveChannelCommand { get; private set; }
    private bool CanRemoveChannelCommand(object o)
    {
        if (LoggingManager.Instance.Active)
        {
            var errorDialogViewModel = new ErrorDialogViewModel("Cannot delete channel while logging.");
            _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);

            return false;
        }
        return true;
    }

    public ICommand ToggleGraphFlyoutCommand { get; private set; }
    private bool CanToggleGraphFlyoutCommand(object o)
    {
        return true;
    }

    public ICommand ToggleChannelFlyoutCommand { get; private set; }
    private bool CanToggleChannelFlyoutCommand(object o)
    {
        return true;
    }

    public ICommand ToggleDeviceFlyoutCommand { get; private set; }
    private bool CanToggleDeviceFlyoutCommand(object o)
    {
        return true;
    }

    public ICommand OpenLiveGraphSettingsCommand { get; private set; }
    private bool CanOpenLiveGraphSettings(object o)
    {
        return true;
    }

    public ICommand OpenDeviceSettingsCommand { get; private set; }
    private bool CanOpenDeviceSettings(object o)
    {
        return true;
    }

    public ICommand OpenFirmwareUpdateCommand { get; private set; }
    public bool CanOpenFirmwareUpdateSettings(object o)
    {
        return true;
    }

    public ICommand OpenChannelSettingsCommand { get; private set; }
    private bool CanOpenChannelSettings(object o)
    {
        return true;
    }
    public ICommand IsprofileActiveCommand { get; private set; }
    private bool CanOpenProfileSettings(object o)
    {
        return true;
    }

    public ICommand NotificationCommand { get; private set; }
    private bool CanOpenNotification(object o)
    {
        return true;
    }

    public ICommand OpenProfileSettingsCommand { get; private set; }
    private bool CanIsprofileActive(object o)
    {
        return true;
    }

    public ICommand OpenLogSummaryCommand { get; private set; }
    private bool CanOpenLogSummary(object o)
    {
        return true;
    }

    public ICommand OpenLoggingSessionSettingsCommand { get; private set; }
    private bool CanOpenLoggingSessionSettings(object o)
    {
        return true;
    }

    public ICommand DisconnectDeviceCommand { get; private set; }
    private bool CanDisconnectDevice(object o)
    {
        if (LoggingManager.Instance.Active)
        {
            var errorDialogViewModel = new ErrorDialogViewModel("Cannot disconnect streamingDevice while logging.");
            _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);

            return false;
        }
        return true;
    }

    public ICommand UpdateNetworkConfigurationCommand { get; private set; }
    private bool CanUpdateNetworkConfiguration(object o)
    {
        return true;
    }

    public ICommand ShutdownCommand { get; private set; }
    private bool CanShutdown(object o)
    {
        return true;
    }

    public ICommand DisplayLoggingSessionCommand { get; private set; }
    private bool CanDisplayLoggingSession(object o)
    {
        return true;
    }

    public ICommand ExportLoggingSessionCommand { get; private set; }
    private bool CanExportLoggingSession(object o)
    {
        return true;
    }

    public ICommand ExportAllLoggingSessionCommand { get; private set; }
    private bool CanExportAllLoggingSession(object o)
    {
        return true;
    }

    public ICommand DeleteLoggingSessionCommand { get; private set; }
    private bool CanDeleteLoggingSession(object o)
    {
        return true;
    }

    public ICommand DeleteAllLoggingSessionCommand { get; private set; }
    private bool CanDeleteAllLoggingSession(object o)
    {
        return true;
    }

    public ICommand RebootSelectedDeviceCommand { get; private set; }
    private bool CanRebootSelectedDevice(object o)
    {
        return true;
    }

    public ICommand OpenHelpCommand { get; private set; }
    private bool CanOpenHelp(object o)
    {
        return true;
    }

    public ICommand BrowseForFirmwareCommand { get; private set; }
    private bool CanBrowseForFirmware(object o)
    {
        return true;
    }
    #endregion

    #region Register Command 
    public void RegisterCommands()
    {
        ShowAddProfileConfirmationDialogCommand = new DelegateCommand(ShowAddProfileConfirmation, CanShowAddProfileConfirmationDialogCommand);
        ShowAddProfileDialogCommand = new DelegateCommand(ShowAddProfileDialog, CanShowAddProfileDialog);
        ShowConnectionDialogCommand = new DelegateCommand(ShowConnectionDialog, CanShowConnectionDialog);
        ShowAddChannelDialogCommand = new DelegateCommand(ShowAddChannelDialog, CanShowAddChannelDialog);
        ShowSelectColorDialogCommand = new DelegateCommand(ShowSelectColorDialog, CanShowSelectColorDialogCommand);
        RemoveProfileCommand = new DelegateCommand(RemoveProfile, CanRemoveProfileCommand);
        RemoveChannelCommand = new DelegateCommand(RemoveChannel, CanRemoveChannelCommand);
        OpenLiveGraphSettingsCommand = new DelegateCommand(OpenLiveGraphSettings, CanOpenLiveGraphSettings);
        OpenDeviceSettingsCommand = new DelegateCommand(OpenDeviceSettings, CanOpenDeviceSettings);
        OpenChannelSettingsCommand = new DelegateCommand(OpenChannelSettings, CanOpenChannelSettings);
        OpenProfileSettingsCommand = new DelegateCommand(OpenProfileSettings, CanOpenProfileSettings);
        NotificationCommand = new DelegateCommand(OpenNotifications, CanOpenNotification);
        IsprofileActiveCommand = new DelegateCommand(GetSelectedProfileActive, CanIsprofileActive);
        OpenLogSummaryCommand = new DelegateCommand(OpenLogSummary, CanOpenLogSummary);
        OpenLoggingSessionSettingsCommand = new DelegateCommand(OpenLoggingSessionSettings, CanOpenLoggingSessionSettings);
        ShowDAQifiSettingsDialogCommand = new DelegateCommand(ShowDAQifiSettingsDialog, CanShowDAQifiSettingsDialog);
        DisconnectDeviceCommand = new DelegateCommand(DisconnectDevice, CanDisconnectDevice);
        UpdateNetworkConfigurationCommand = new DelegateCommand(UpdateNetworkConfiguration, CanUpdateNetworkConfiguration);
        ShutdownCommand = new DelegateCommand(Shutdown, CanShutdown);
        DisplayLoggingSessionCommand = new DelegateCommand(DisplayLoggingSession, CanDisplayLoggingSession);
        ExportLoggingSessionCommand = new DelegateCommand(ExportLoggingSession, CanExportLoggingSession);
        ExportAllLoggingSessionCommand = new DelegateCommand(ExportAllLoggingSession, CanExportAllLoggingSession);
        DeleteLoggingSessionCommand = new DelegateCommand(DeleteLoggingSession, CanDeleteLoggingSession);
        DeleteAllLoggingSessionCommand = new DelegateCommand(DeleteAllLoggingSession, CanDeleteAllLoggingSession);
        RebootSelectedDeviceCommand = new DelegateCommand(RebootSelectedDevice, CanRebootSelectedDevice);
        OpenHelpCommand = new DelegateCommand(OpenHelp, CanOpenHelp);
        BrowseForFirmwareCommand = new DelegateCommand(BrowseForFirmware, CanBrowseForFirmware);
        UploadFirmwareCommand = new DelegateCommand(UploadFirmware, CanUploadFirmware);
        OpenFirmwareUpdateCommand = new DelegateCommand(OpenFirmwareUpdateSettings, CanOpenFirmwareUpdateSettings);
        HostCommands.ShutdownCommand.RegisterCommand(ShutdownCommand);
    }
    #endregion

    #region Command Callback Methods

    #region Updload firmware and update processes
    void UploadFirmwareProgressChanged(object sender, ProgressChangedEventArgs e)
    {
        UploadFirmwareProgress = e.ProgressPercentage;
    }
    private BackgroundWorker _updateWiFiBackgroundWorker;
    private async void InitializeUpdateWiFiBackgroundWorker()
    {
        _updateWiFiBackgroundWorker = new BackgroundWorker
        {
            WorkerReportsProgress = true
        };
        _updateWiFiBackgroundWorker.DoWork += UpdateWiFiBackgroundWorkerDoWork;
        _updateWiFiBackgroundWorker.ProgressChanged += UpdateWiFiBackgroundWorkerProgressChanged;
        _updateWiFiBackgroundWorker.RunWorkerCompleted += UpdateWiFiBackgroundWorkerRunWorkerCompleted;
        if (!_updateWiFiBackgroundWorker.IsBusy)
        {
            UploadWiFiProgress = 0;
            _updateWiFiBackgroundWorker.RunWorkerAsync();
        }
    }

    private void UpdateWiFiBackgroundWorkerDoWork(object sender, DoWorkEventArgs e)
    {
        try 
        {
            var wifiUpdater = new WifiModuleUpdater();
            var progress = new Progress<int>(percent => _updateWiFiBackgroundWorker.ReportProgress(percent));

            if (_selectedDevice is not IFirmwareUpdateDevice updateDevice)
            {
                throw new InvalidOperationException("Selected device does not support firmware updates");
            }

            var task = wifiUpdater.UpdateWifiModuleAsync(updateDevice, progress);
            task.Wait();
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Error updating WiFi firmware");
            e.Result = ex;
        }
    }

    private void UpdateWiFiBackgroundWorkerProgressChanged(object sender, ProgressChangedEventArgs e)
    {
        UploadWiFiProgress = e.ProgressPercentage;
    }

    private void UpdateWiFiBackgroundWorkerRunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
    {
        IsFirmwareUploading = false;
        if (e.Error != null || e.Result is Exception)
        {
            AppLogger.Instance.Error(e.Error ?? (Exception)e.Result, "Problem Uploading WiFi Firmware");
            HasErrorOccured = true;
                
            Application.Current.Dispatcher.Invoke(() =>
            {
                var errorDialogViewModel = new ErrorDialogViewModel("WiFi firmware update failed. Please try again.");
                _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
            });
        }
        else
        {
            IsUploadComplete = true;
            ShowUploadSuccessMessage();
        }
    }
    private void HandleFirmwareUploadCompleted(object sender, RunWorkerCompletedEventArgs e)
    {
        if (e.Error != null || e.Cancelled)
        {
            IsFirmwareUploading = false;
            return;
        }
            
        var isManualUpload = (bool)e.Result;
        
        if (isManualUpload)
        {
            IsUploadComplete = true;
            ShowUploadSuccessMessage();
        }
        else
        {
            InitializeUpdateWiFiBackgroundWorker();
        }
        IsFirmwareUploading = false;
    }

    private void UploadFirmware(object o)
    {
        if (_selectedDevice is not SerialStreamingDevice)
        {
            return;
        }
        
        var isManualUpload = false;
        // Download if a hex file wasn't passed to it.
        if (string.IsNullOrEmpty(FirmwareFilePath))
        {
            FirmwareFilePath = new FirmwareDownloader().Download();
        }
        else
        {
            isManualUpload = true;
        }

        if (string.IsNullOrEmpty(FirmwareFilePath))
        {
            _appLogger.Error("Firmware file path is null or empty.");
            return;
        }
        
        (_selectedDevice as SerialStreamingDevice)!.ForceBootloader();
        _selectedDevice.Disconnect();
        
        // Once the DAQiFi resets, the COM serial port is closed,
        // and the HID port for managing the bootloader must be found
        StartConnectionFinders();

        // Update the variable 'HasNoHidDevices' in a background task
        var bw2 = new BackgroundWorker();
        bw2.DoWork += delegate
        {
            while (HasNoHidDevices)
            {
                Thread.Sleep(2000);
                if (HasNoHidDevices == false)
                {
                    // Connect HID if it was found before              
                    var hidFirmwareDevice = ConnectHid(AvailableHidDevices);
                    if (hidFirmwareDevice != null)
                    {
                        _bootloader = new Pic32Bootloader(hidFirmwareDevice.Device);
                        _bootloader.PropertyChanged += OnHidDevicePropertyChanged;
                        _bootloader.RequestVersion();

                        var bw = new BackgroundWorker();
                        bw.DoWork += (sender, e) =>
                        {
                            IsFirmwareUploading = true;
                            if (string.IsNullOrWhiteSpace(FirmwareFilePath))
                            {
                                return;
                            }

                            if (!File.Exists(FirmwareFilePath))
                            {
                                return;
                            }
                                
                            if (_bootloader != null)
                            {
                                _bootloader.LoadFirmware(FirmwareFilePath, bw);
                            }
                            e.Result = isManualUpload;
                        };
                        bw.WorkerReportsProgress = true;
                        bw.ProgressChanged += UploadFirmwareProgressChanged;
                        bw.RunWorkerCompleted += HandleFirmwareUploadCompleted;
                        bw.RunWorkerAsync();
                    }
                }
            }
        };
        bw2.RunWorkerAsync();
    }
    #endregion
    private void ShowConnectionDialog(object o)
    {
        _connectionDialogViewModel = new ConnectionDialogViewModel();
        _connectionDialogViewModel.StartConnectionFinders();
        _dialogService.ShowDialog<ConnectionDialog>(this, _connectionDialogViewModel);
    }

    private void ShowAddChannelDialog(object o)
    {
        var addChannelDialogViewModel = new AddChannelDialogViewModel();
        _dialogService.ShowDialog<AddChannelDialog>(this, addChannelDialogViewModel);
    }
    private void ShowSelectColorDialog(object o)
    {
        var item = o as IColorable;
        if (item == null)
        {
            _appLogger.Error("Cannot set the color of an item that does not implement IHasColor.");
        }

        var selectColorDialogViewModel = new SelectColorDialogViewModel(item);
        _dialogService.ShowDialog<SelectColorDialog>(this, selectColorDialogViewModel);
    }

    private void ShowDAQifiSettingsDialog(object o)
    {
        var settingsViewModel = new SettingsViewModel();
        _dialogService.ShowDialog<SettingsDialog>(this, settingsViewModel);
    }

    private void RemoveChannel(object o)
    {
        var channelToRemove = o as IChannel;
        var device = ConnectionManager.Instance.ConnectedDevices.FirstOrDefault(x => x.DeviceSerialNo == channelToRemove.DeviceSerialNo);
        var channel = device.DataChannels.FirstOrDefault(x => x.DeviceSerialNo == channelToRemove.DeviceSerialNo && x.Name == channelToRemove.Name);
        if (device != null && channel != null)
        {
            LoggingManager.Instance.Unsubscribe(channel);
            device.RemoveChannel(channel);
            return;
        }

    }

    private void DisconnectDevice(object o)
    {
        if (!(o is IStreamingDevice deviceToRemove)) { return; }

        foreach (var channel in deviceToRemove.DataChannels)
        {
            if (channel.IsActive)
            {
                LoggingManager.Instance.Unsubscribe(channel);
            }
        }
        ConnectionManager.Instance.Disconnect(deviceToRemove);
        RemoveNotification(deviceToRemove);
    }

    public void Shutdown(object o)
    {
        foreach (var device in ConnectedDevices)
        {
            device.Disconnect();
        }
    }

    public void UpdateNetworkConfiguration(object o)
    {
        SelectedDevice.UpdateNetworkConfiguration();
        _dialogService.ShowDialog<SuccessDialog>(this, new SuccessDialogViewModel("WiFi settings updated."));
    }

    public void BrowseForFirmware(object o)
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

    private void OpenLiveGraphSettings(object o)
    {
        CloseFlyouts();
        IsLiveGraphSettingsOpen = true;
    }

    private void OpenDeviceSettings(object o)
    {
        var item = o as IStreamingDevice;
        if (item == null)
        {
            _appLogger.Error("Error opening streamingDevice settings");
        }

        CloseFlyouts();
        SelectedDevice = item;
        IsDeviceSettingsOpen = true;
    }

    private void OpenFirmwareUpdateSettings(object o)
    {
        var item = o as IStreamingDevice;
        if (item == null)
        {
            _appLogger.Error("Error opening firmware settings");
        }
        CloseFlyouts();
        SelectedDevice = item;
        IsFirmwareUpdatationFlyoutOpen = true;

    }

    private void OpenChannelSettings(object o)
    {
        if (!(o is IChannel item))
        {
            _appLogger.Error("Error opening channel settings");
            return;
        }

        CloseFlyouts();
        SelectedChannel = item;
        IsChannelSettingsOpen = true;
    }

    private void OpenLogSummary(object o)
    {
        CloseFlyouts();
        IsLogSummaryOpen = true;
    }

    private void OpenLoggingSessionSettings(object o)
    {
        var item = o as LoggingSession;
        if (item == null) { _appLogger.Error("Error opening logging session settings"); }

        CloseFlyouts();
        SelectedLoggingSession = item;
        if (item.Name.Length == 0)
        {
            LoggedSessionName = "Session_" + item.ID; 
        }
        else
        {
            LoggedSessionName = item.Name;
        }
        LoggedSessionName = item.Name;
        IsLoggingSessionSettingsOpen = true;
    }

    private void DisplayLoggingSession(object o)
    {
        if (!(o is LoggingSession session))
        {
            DbLogger.ClearPlot();
            return;
        }

        IsLoggedDataBusy = true;
        LoggedDataBusyReason = "Loading " + session.Name;
        var bw = new BackgroundWorker();
        bw.DoWork += delegate
        {
            try
            {
                DbLogger.DisplayLoggingSession(session);
            }
            finally
            {
                IsLoggedDataBusy = false;
            }
        };

        bw.RunWorkerAsync();
    }

    private void ExportLoggingSession(object o)
    {
        if (!(o is LoggingSession session))
        {
            _appLogger.Error("Error exporting logging session");
            return;
        }

        var exportDialogViewModel = new ExportDialogViewModel(session.ID);
        _dialogService.ShowDialog<ExportDialog>(this, exportDialogViewModel);
    }

    private void ExportAllLoggingSession(object o)
    {
        if (LoggingSessions.Count == 0)
        {
            _appLogger.Error("Error exporting all logging sessions");
            return;
        }

        var exportDialogViewModel = new ExportDialogViewModel(LoggingSessions);
        _dialogService.ShowDialog<ExportDialog>(this, exportDialogViewModel);
    }

    private async void DeleteLoggingSession(object o)
    {
        try
        {
            if (!(o is LoggingSession session))
            {
                _appLogger.Error("Error deleting logging session");
                return;
            }

            var result = await ShowMessage("Delete Confirmation", "Are you sure you want to delete " + session.Name + "?", MessageDialogStyle.AffirmativeAndNegative).ConfigureAwait(false);
            if (result != MessageDialogResult.Affirmative)
            {
                return;
            }

            IsLoggedDataBusy = true;
            LoggedDataBusyReason = "Deleting Logging Session #" + session.ID;
            var bw = new BackgroundWorker();
            bw.DoWork += delegate
            {
                try
                {
                    DbLogger.DeleteLoggingSession(session);
                    Application.Current.Dispatcher.Invoke(delegate
                    {
                        LoggingSessions.Remove(session);
                    });
                    OnPropertyChanged("LoggingSessions");
                }
                finally
                {
                    IsLoggedDataBusy = false;
                }
            };

            bw.RunWorkerAsync();
        }
        catch (System.Exception ex)
        {
            _appLogger.Error(ex, "Error Deleting Logging Session");
        }
    }

    private async void DeleteAllLoggingSession(object o)
    {
        try
        {
            if (LoggingSessions.Count == 0)
            {
                _appLogger.Error("Error deleting logging session");
                return;
            }

            var result = await ShowMessage("Delete Confirmation", "Are you sure you want to delete all logging sessions?", MessageDialogStyle.AffirmativeAndNegative).ConfigureAwait(false);
            if (result != MessageDialogResult.Affirmative)
            {
                return;
            }

            IsLoggedDataBusy = true;
            LoggedDataBusyReason = "Deleting All Logging Sessions";
            var bw = new BackgroundWorker();
            bw.DoWork += delegate
            {
                try
                {
                    while (LoggingSessions.Count > 0)
                    {
                        var session = LoggingSessions.ElementAt(0);
                        DbLogger.DeleteLoggingSession(session);
                        Application.Current.Dispatcher.Invoke(delegate
                        {
                            LoggingSessions.Remove(session);
                        });
                    }
                    OnPropertyChanged("LoggingSessions");
                }
                finally
                {
                    IsLoggedDataBusy = false;
                }
            };

            bw.RunWorkerAsync();
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Error Deleting All Logging Session");
        }
    }

    private void RebootSelectedDevice(object o)
    {
        if (!(o is IStreamingDevice deviceToReboot)) { return; }

        if (deviceToReboot.DataChannels != null)
        {
            foreach (var channel in deviceToReboot.DataChannels)
            {
                if (channel.IsActive)
                {
                    LoggingManager.Instance.Unsubscribe(channel);
                }
            }
        }

        ConnectionManager.Instance.Reboot(deviceToReboot);
    }

    private void OpenHelp(object o)
    {
        try
        {
            var url = "https://www.daqifi.com/support";

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
    #endregion

    #region Methods
    public void StartConnectionFinders()
    {
        _hidDeviceFinder = new HidDeviceFinder();
        _hidDeviceFinder.OnDeviceFound += HandleHidDeviceFound;
        _hidDeviceFinder.OnDeviceRemoved += HandleHidDeviceRemoved;
        _hidDeviceFinder.Start();
    }

    public void Close()
    {
        _hidDeviceFinder?.Stop();
    }

    private HidFirmwareDevice ConnectHid(object selectedItems)
    {
        // Read variable
        HidFirmwareDevice hidDevice = null;
        var selectedDevices = ((IEnumerable)selectedItems).Cast<HidFirmwareDevice>();
        hidDevice = selectedDevices.FirstOrDefault();
        return hidDevice;
    }

    private void HandleHidDeviceFound(object sender, IDevice device)
    {
        if (!(device is HidFirmwareDevice hidDevice))
        {
            return;
        }

        Application.Current.Dispatcher.Invoke(() =>
        {
            AvailableHidDevices.Add(hidDevice);
            if (HasNoHidDevices) { HasNoHidDevices = false; }
        });
    }

    private void HandleHidDeviceRemoved(object sender, IDevice device)
    {
        if (!(device is HidFirmwareDevice hidDevice)) { return; }

        Application.Current.Dispatcher.Invoke(() =>
        {
            AvailableHidDevices.Remove(hidDevice);
            if (AvailableHidDevices.Count == 0) { HasNoHidDevices = true; }
        });
    }
    public async Task UpdateConnectedDeviceUI()
    {
        UploadFirmwareProgress = 0;
        UploadWiFiProgress = 0;

        foreach (var connectedDevice in ConnectionManager.Instance.ConnectedDevices)
        {
            var SerailDeviceProperty = connectedDevice.GetType().GetProperty("DeviceVersion");
            var DeviceVersion = SerailDeviceProperty.GetValue(connectedDevice)?.ToString();
            if (DeviceVersion != latestFirmwareVersion && (connectedDevice.Name.StartsWith("COM")))
            {
                connectedDevice.IsFirmwareOutdated = true;
            }

            ConnectedDevices.Add(connectedDevice);
        }
    }
    public async void UpdateUi(object sender, PropertyChangedEventArgs args)
    {

        switch (args.PropertyName)
        {
            case "SubscribedProfiles":

                if (LoggingManager.Instance.SubscribedProfiles.Count == 0)
                { profiles.Clear(); }
                foreach (Profile connectedProfiles in LoggingManager.Instance.SubscribedProfiles)
                {
                    if (!profiles.Where(x => x.ProfileId == connectedProfiles.ProfileId).Any())
                    {
                        profiles.Add(connectedProfiles);
                    }
                }
                break;
            case "ConnectedDevices":
                ConnectedDevices.Clear();
                await UpdateConnectedDeviceUI();

                GetUpdateProfileAvailableDevice();
                break;
            case "SubscribedChannels":
                ActiveChannels.Clear();
                ActiveInputChannels.Clear();
                foreach (var channel in LoggingManager.Instance.SubscribedChannels)
                {
                    if (!channel.IsOutput)
                    {
                        ActiveInputChannels.Add(channel);
                    }
                    ActiveChannels.Add(channel);
                }
                break;
            case "LoggingSessions":
                LoggingSessions.Clear();
                foreach (var session in LoggingManager.Instance.LoggingSessions)
                {
                    LoggingSessions.Add(session);
                }
                break;
            case "NotificationCount":
                var data = _versionNotification;
                NotificationCount = data.NotificationCount;
                if (NotificationCount > 0)
                {
                    VersionName = data.VersionNumber;
                    var notify = new Notifications()
                    {
                        isFirmwareUpdate = false,
                        DeviceSerialNo = null,
                        Message = $"Please update latest application version:  {VersionName}",
                        Link = "https://github.com/daqifi/daqifi-desktop/releases"
                    };
                    if (!notificationlist.Any(n => n.Message == notify.Message || n.Link == notify.Link))
                    {
                        notificationlist.Add(notify);
                    }
                }
                break;
            case "NotifyConnection":
                var DeviceConnection = ConnectionManager.Instance.NotifyConnection;
                if (DeviceConnection)
                {
                    var errorDialogViewModel = new ErrorDialogViewModel("Device disconnected..");
                    if (errorDialogViewModel != null)
                    {
                        // To do  work giving error 
                        //_dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);

                    }
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

    #region New Enhancements and developement
    /// <summary>
    /// Show add profile dialog
    /// </summary>
    /// <param name="obj"></param>
    public void ShowAddProfileDialog(object obj)
    {
        try
        {
            var addProfileDialogViewModel = new AddProfileDialogViewModel();
            _dialogService.ShowDialog<AddprofileDialog>(this, addProfileDialogViewModel);
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Error opening add profile dialog");
        }

    }
    /// <summary>
    /// show save profile confirmation for new setting or save current settings
    /// </summary>
    /// <param name="obj"></param>
    private void ShowAddProfileConfirmation(object obj)
    {
        try
        {
            LoggingManager.Instance.AddAndRemoveProfileXml(null, false);
            var addProfileConfirmationDialogViewModel = new AddProfileConfirmationDialogViewModel();
            _dialogService.ShowDialog<AddProfileConfirmationDialog>(this, addProfileConfirmationDialogViewModel);
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Error opening confirmation dialog");
        }

    }
    /// <summary>
    ///Get avaliable devices 
    /// </summary>
    public void GetUpdateProfileAvailableDevice()
    {
        foreach (var device in ConnectionManager.Instance.ConnectedDevices)
        {
            AvailableDevices.Add(device);
        }
    }

    #region Firmware version checking methods 

    private string latestFirmwareVersion;
    public async Task GetFirmwareupdatationList()
    {
        var connectedDevices = ConnectionManager.Instance.ConnectedDevices;
        if (connectedDevices.Count > 0)
        {

            var ldata = await FirmwareUpdatationManager.Instance.CheckFirmwareVersion();
            latestFirmwareVersion = ldata;

            if (latestFirmwareVersion == null)
            {
                return;
            }
            foreach (var device in connectedDevices)
            {
                var deviceVersion = new Version(device.DeviceVersion);
                var latestVersion = new Version(latestFirmwareVersion);
                if (device.DeviceSerialNo != null && deviceVersion < latestVersion)
                {
                    AddNotification(device, latestFirmwareVersion);
                }
            }
        }
    }
    private void RemoveNotification()
    {
        foreach (var notification in notificationlist.ToList())
        {
            var deviceIsDisconnected = !ConnectionManager.Instance.ConnectedDevices
                .Any(device => device.DeviceSerialNo == notification.DeviceSerialNo);

            if (deviceIsDisconnected)
            {
                notificationlist.Remove(notification);
            }
        }
        NotificationCount = notificationlist.Count;
    }

    private void AddNotification(IStreamingDevice device, string LatestFirmware)
    {
        var message = $"Device With Serial {device.DeviceSerialNo} has Outdated Firmware. Please Update to Version {LatestFirmware}.";

        var existingNotification = notificationlist.FirstOrDefault(n => n.DeviceSerialNo != null
                                                                        && n.isFirmwareUpdate
                                                                        && n.DeviceSerialNo == device.DeviceSerialNo);

        if (existingNotification == null)
        {
            notificationlist.Add(new Notifications
            {
                DeviceSerialNo = device.DeviceSerialNo,
                Message = message,
                isFirmwareUpdate = true
            });
        }

        NotificationCount = notificationlist.Count;
    }
    private void RemoveNotification(IStreamingDevice deviceToRemove)
    {
        var notificationsToRemove = notificationlist.FirstOrDefault(x => x.DeviceSerialNo != null && x.DeviceSerialNo == deviceToRemove.DeviceSerialNo && x.isFirmwareUpdate);
        notificationlist.Remove(notificationsToRemove);
        NotificationCount = notificationlist.Count;
    }

    #endregion
    /// <summary>
    /// Remove profile 
    /// </summary>
    /// <param name="obj"></param>
    private void RemoveProfile(object obj)
    {
        var ProfileToRemove = obj as Profile;
        if (LoggingManager.Instance.Active)
        {
            var errorDialogViewModel = new ErrorDialogViewModel("Cannot remove profile while logging");
            _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
            return;
        }
        if (ProfileToRemove.IsProfileActive)
        {
            var errorDialogViewModel = new ErrorDialogViewModel("Cannot remove profile while profile is active");
            _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
            return;
        }
        if (profiles.Any(x => x.IsProfileActive))
        {
            var errorDialogViewModel = new ErrorDialogViewModel("Cannot remove profile while profile is active");
            _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
            return;
        }
        LoggingManager.Instance.UnsubscribeProfile(ProfileToRemove);
        ActiveChannels.Clear();
        ActiveInputChannels.Clear();
        profiles.Remove(ProfileToRemove);
        return;
    }
    /// <summary>
    /// Open notification
    /// </summary>
    /// <param name="o"></param>
    private void OpenNotifications(object o)
    {
        IsNotificationsOpen = true;
    }
    /// <summary>
    /// Open profile settings flyout 
    /// </summary
    /// <param name="obj"></param>
    private void OpenProfileSettings(object obj)
    {
        if (!(obj is Profile item))
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
        if (item.IsProfileActive)
        {
            var errorDialogViewModel = new ErrorDialogViewModel("Cannot edit profile while profile is active");
            _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
            return;
        }
        SelectedProfile = item;
        LoggingManager.Instance.SelectedProfile = item;
        LoggingManager.Instance.Flag = false;
        LoggingManager.Instance.SelectedProfileChannels.Clear();
        LoggingManager.Instance.SelectedProfileDevices.Clear();

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
            if (ConnectionManager.Instance.ConnectedDevices.Any(x => x.DeviceSerialNo == selectedDevice.DeviceSerialNo))
            {
                var device = ConnectionManager.Instance.ConnectedDevices.FirstOrDefault(x => x.DeviceSerialNo == selectedDevice.DeviceSerialNo);
                if (device != null)
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
                            // Add channels not in the selected profile channels
                            //profileChannel.SerialNo = selectedDevice.DeviceSerialNo; // Associate the serial number
                            LoggingManager.Instance.SelectedProfileChannels.Add(profileChannel);
                        }

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
    /// <summary>
    /// Get Available Channels
    /// </summary>
    /// <param name="device"></param>
    public void GetAvailableChannels(IStreamingDevice device)
    {
        AvailableChannels.Clear();

        foreach (var channel in device.DataChannels)
        {
            AvailableChannels.Add(channel);
        }
    }
    /// <summary>
    /// Save current session profile Settings 
    /// </summary>
    public void SaveExistingSetting()
    {
        try
        {
            if (ConnectedDevices.Count > 2)
            {
                var errorDialogViewModel = new ErrorDialogViewModel("Cannot add profile with  connected devices more than two");
                _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
                return;
            }
            var addProfileModel = new AddProfileModel
            {
                ProfileList = new List<Profile>()
            };

            var newProfile = new Profile
            {

                Name = "DaqifiLastSessionProfile",
                ProfileId = Guid.NewGuid(),
                CreatedOn = DateTime.Now,
                Devices = new ObservableCollection<ProfileDevice>()
            };

            foreach (var selectedDevice in ConnectedDevices)
            {
                if (selectedDevice?.DataChannels?.Count > 0)
                {
                    var device = new ProfileDevice
                    {
                        MACAddress = selectedDevice.MacAddress,
                        DeviceName = selectedDevice.Name,
                        DevicePartName = selectedDevice.DevicePartNumber,
                        DeviceSerialNo = selectedDevice.DeviceSerialNo,
                        SamplingFrequency = selectedDevice.StreamingFrequency,
                        //SamplingFrequency = SelectedStreamingFrequency,
                        Channels = new List<ProfileChannel>()
                    };

                    foreach (var dataChannel in selectedDevice.DataChannels)
                    {
                        if (dataChannel.IsActive)
                        {
                            var profileChannel = new ProfileChannel
                            {
                                Name = dataChannel.Name,
                                Type = dataChannel.TypeString.ToString(),
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

    /// <summary>
    /// Get Selected profile active 
    /// </summary>
    /// <param name="obj"></param>
    private void GetSelectedProfileActive(object obj)
    {
        try
        {
            if (!(obj is Profile item))
            {
                var errorDialogViewModel = new ErrorDialogViewModel("Error Activating Profile.");
                _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
                _appLogger.Error("Error Activating Profile");
                return;
            }

            // Check for multiple active profiles
            var anyActiveProfile = profiles.FirstOrDefault(x => x.IsProfileActive);
            if (anyActiveProfile != null && anyActiveProfile.ProfileId != item.ProfileId)
            {
                var errorDialogViewModel = new ErrorDialogViewModel("Multiple Profiles Cannot be Active.");
                _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
                _appLogger.Error("Multiple Profiles Cannot be Active.");
                return;
            }

            var connectedDevices = ConnectedDevices
                .Where(cd => item.Devices.Any(id => id.DeviceSerialNo == cd.DeviceSerialNo))
                .ToList();


            if (connectedDevices == null || connectedDevices.Count == 0)
            {
                var errorDialogViewModel = new ErrorDialogViewModel("Profile cannot be active. No connected devices.");
                _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
                return;
            }

            // Check if logging is active
            if (LoggingManager.Instance.Active)
            {
                var errorDialogViewModel = new ErrorDialogViewModel("Profile cannot be active while logging.");
                _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
                return;
            }

            // Iterate through connected devices
            foreach (var connectedDevice in connectedDevices)
            {
                SelectedDevice = connectedDevice;
                UpdateProfileSelectedDevice = SelectedDevice;

                // Update device frequencies
                var matchingDevice = item.Devices.FirstOrDefault(device => device.DeviceSerialNo == connectedDevice.DeviceSerialNo);
                if (matchingDevice != null)
                {
                    SelectedStreamingFrequency = matchingDevice.SamplingFrequency;
                    connectedDevice.StreamingFrequency = matchingDevice.SamplingFrequency;
                }

                // Handle profile activation or deactivation
                foreach (var device in item.Devices)
                {
                    foreach (var channel in device.Channels)
                    {
                        // Match both device and channel information to ensure distinct mapping
                        var profileChannel = AvailableChannels
                            .FirstOrDefault(x => x.Name == channel.Name.Trim() &&
                                                 x.TypeString == channel.Type.Trim() &&
                                                 x.DeviceSerialNo == device.DeviceSerialNo &&
                                                 channel.IsChannelActive);

                        if (profileChannel != null)
                        {
                            if (item.IsProfileActive)
                            {
                                LoggingManager.Instance.Unsubscribe(profileChannel);
                            }
                            else
                            {
                                LoggingManager.Instance.Subscribe(profileChannel);
                            }
                        }
                    }
                }
            }
            // Toggle profile's active state
            item.IsProfileActive = !item.IsProfileActive;
        }
        catch (Exception ex)
        {
            _appLogger.Error("Error activating Profile: " + ex.Message);
        }
    }

    private void ShowUploadSuccessMessage()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var successDialogViewModel =
                new SuccessDialogViewModel("Firmware update completed successfully.");
            _dialogService.ShowDialog<SuccessDialog>(this, successDialogViewModel);
        });
        CloseFlyouts();
    }

    #endregion

    #endregion
}