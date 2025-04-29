using Daqifi.Desktop.Bootloader;
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
using CommunityToolkit.Mvvm.ComponentModel;
using Daqifi.Desktop.Device.SerialDevice;
using Application = System.Windows.Application;
using File = System.IO.File;

namespace Daqifi.Desktop.ViewModels;

public partial class DaqifiViewModel : ObservableObject
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
    private int _width = 800;
    private int _height = 600;
    private int _selectedIndex;
    private int _selectedStreamingFrequency;
    private WindowState _viewWindowState;
    private readonly IDialogService _dialogService;
    private IStreamingDevice _selectedDevice;
    private VersionNotification? _versionNotification;
    private IStreamingDevice _updateProfileSelectedDevice;
    [ObservableProperty]
    private IChannel _selectedChannel;
    [ObservableProperty]
    private Profile _selectedProfile;
    [ObservableProperty]
    private LoggingSession _selectedLoggingSession;
    private bool _isLogging;
    private bool _canToggleLogging;
    [ObservableProperty]
    private string _loggedDataBusyReason;
    [ObservableProperty]
    private string _firmwareFilePath;
    private Pic32Bootloader _bootloader;
    [ObservableProperty]
    private string _version;
    [ObservableProperty]
    private bool _isFirmwareUploading;
    [ObservableProperty]
    private bool _isUploadComplete;
    [ObservableProperty]
    private bool _hasErrorOccured;
    private int _uploadFirmwareProgress;
    [ObservableProperty]
    private int _uploadWiFiProgress;
    private HidDeviceFinder _hidDeviceFinder;
    [ObservableProperty]
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

    public ObservableCollection<Notifications> NotificationList { get; } = [];
    public ObservableCollection<IChannel> ActiveChannels { get; } = [];
    public ObservableCollection<IChannel> ActiveInputChannels { get; } = [];
    public ObservableCollection<LoggingSession> LoggingSessions => LoggingManager.Instance.LoggingSessions;

    public PlotLogger Plotter { get; private set; }
    public DatabaseLogger DbLogger { get; private set; }
    public SummaryLogger SummaryLogger { get; private set; }
    public ObservableCollection<IStreamingDevice> AvailableDevices { get; } = [];
    public ObservableCollection<IChannel> AvailableChannels { get; } = [];

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

    public string UploadFirmwareProgressText => ($"Upload Progress: {UploadFirmwareProgress}%");
    
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
            _isLogging = value;
            LoggingManager.Instance.Active = value;
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

    public bool IsNotLogging => !IsLogging;

    [ObservableProperty]
    private int _notificationCount;
    
    [ObservableProperty]
    private string _versionName;

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
                return SystemParameters.WorkArea.Width - SidePanelWidth;
            }
            return _width - SidePanelWidth;
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
            return _height - TopToolbarHeight;
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
    // LoggedSessionName property is generated by [ObservableProperty]

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

    // IsLogToDeviceMode property needs manual implementation (private set)
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
                        var savedLoggingSessions = new ObservableCollection<LoggingSession>();
                        var previousSampleSessions = (from s in context.Sessions select s).ToList();
                        foreach (var session in previousSampleSessions)
                        {
                            if (!savedLoggingSessions.Any(ls => ls.ID == session.ID))
                            {
                                savedLoggingSessions.Add(session);
                            }
                        }
                        if (LoggingManager.Instance.LoggingSessions == null || !LoggingManager.Instance.LoggingSessions.Any())
                        {
                            LoggingManager.Instance.LoggingSessions = savedLoggingSessions;
                        }
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

    private void RegisterCommands()
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
        if (o is not IStreamingDevice deviceToRemove)
        {
            return;
        }

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
        if (o is not IChannel item)
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
        if (o is not LoggingSession session)
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
            if (!(o is LoggingSession sessionToDelete))
            {
                _appLogger.Error("Error deleting logging session: Invalid object provided.");
                return;
            }

            var result = await ShowMessage("Delete Confirmation", $"Are you sure you want to delete {sessionToDelete.Name}?", MessageDialogStyle.AffirmativeAndNegative).ConfigureAwait(false);
            if (result != MessageDialogResult.Affirmative)
            {
                return;
            }

            IsLoggedDataBusy = true;
            LoggedDataBusyReason = $"Deleting Logging Session #{sessionToDelete.ID}";
            var bw = new BackgroundWorker();
            bw.DoWork += delegate
            {
                bool deleteSucceeded = false;
                try
                {
                    // Pass the original object to the DB operation
                    DbLogger.DeleteLoggingSession(sessionToDelete); 
                    deleteSucceeded = true;
                }
                catch (Exception dbEx)
                {
                    _appLogger.Error(dbEx, $"Failed to delete session {sessionToDelete.ID} from database.");
                    // Optionally show an error message to the user via Dispatcher
                }

                if (deleteSucceeded)
                {
                    // Remove the original object instance from the manager's collection on the UI thread
                    Application.Current.Dispatcher.Invoke(delegate
                    {
                        LoggingManager.Instance.LoggingSessions.Remove(sessionToDelete); // Use original object
                    });
                }
            };
            
            bw.RunWorkerCompleted += (s, e) => 
            {
                IsLoggedDataBusy = false;
            };

            bw.RunWorkerAsync();
        }
        catch (System.Exception ex)
        {
            _appLogger.Error(ex, "Error initiating logging session deletion");
        }
    }

    private async void DeleteAllLoggingSession(object o)
    {
        try
        {
            if (LoggingManager.Instance.LoggingSessions.Count == 0)
            {
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
                var sessionsToDelete = LoggingManager.Instance.LoggingSessions.ToList();
                var successfullyDeletedSessions = new List<LoggingSession>(); // Store the session objects

                foreach(var session in sessionsToDelete)
                {
                    try
                    {
                        DbLogger.DeleteLoggingSession(session);
                        successfullyDeletedSessions.Add(session); // Add the object
                    }
                    catch (Exception dbEx)
                    {
                        _appLogger.Error(dbEx, $"Failed to delete session {session.ID} from database during delete all.");
                        // Continue trying to delete others
                    }
                }

                // Remove all successfully deleted sessions from the collection on the UI thread
                if (successfullyDeletedSessions.Any())
                {
                    Application.Current.Dispatcher.Invoke(delegate
                    {
                        foreach(var sessionToRemove in successfullyDeletedSessions) // Iterate over the objects
                        {
                            LoggingManager.Instance.LoggingSessions.Remove(sessionToRemove);
                        }
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
            _appLogger.Error(ex, "Error initiating deletion of all logging sessions");
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
        var selectedDevices = ((IEnumerable)selectedItems).Cast<HidFirmwareDevice>();
        var hidDevice = selectedDevices.FirstOrDefault();
        return hidDevice;
    }

    private void HandleHidDeviceFound(object sender, IDevice device)
    {
        if (device is not HidFirmwareDevice hidDevice)
        {
            return;
        }

        Application.Current.Dispatcher.Invoke(() =>
        {
            AvailableHidDevices.Add(hidDevice);
            if (HasNoHidDevices)
            {
                HasNoHidDevices = false;
            }
        });
    }

    private void HandleHidDeviceRemoved(object sender, IDevice device)
    {
        if (device is not HidFirmwareDevice hidDevice)
        {
            return;
        }

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
            case "NotificationCount":
                var data = _versionNotification;
                NotificationCount = data.NotificationCount;
                if (NotificationCount > 0)
                {
                    VersionName = data.VersionNumber;
                    var notify = new Notifications
                    {
                        isFirmwareUpdate = false,
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

    private void AddNotification(IStreamingDevice device, string LatestFirmware)
    {
        var message = $"Device With Serial {device.DeviceSerialNo} has Outdated Firmware. Please Update to Version {LatestFirmware}.";

        var existingNotification = NotificationList.FirstOrDefault(n => n.DeviceSerialNo != null
                                                                        && n.isFirmwareUpdate
                                                                        && n.DeviceSerialNo == device.DeviceSerialNo);

        if (existingNotification == null)
        {
            NotificationList.Add(new Notifications
            {
                DeviceSerialNo = device.DeviceSerialNo,
                Message = message,
                isFirmwareUpdate = true
            });
        }

        NotificationCount = NotificationList.Count;
    }
    private void RemoveNotification(IStreamingDevice deviceToRemove)
    {
        var notificationsToRemove = NotificationList.FirstOrDefault(x => x.DeviceSerialNo != null && x.DeviceSerialNo == deviceToRemove.DeviceSerialNo && x.isFirmwareUpdate);
        NotificationList.Remove(notificationsToRemove);
        NotificationCount = NotificationList.Count;
    }

    #endregion
    /// <summary>
    /// Remove profile 
    /// </summary>
    /// <param name="obj"></param>
    private void RemoveProfile(object obj)
    {
        var profileToRemove = obj as Profile;
        if (LoggingManager.Instance.Active)
        {
            var errorDialogViewModel = new ErrorDialogViewModel("Cannot remove profile while logging");
            _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
            return;
        }
        if (profileToRemove.IsProfileActive)
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
        LoggingManager.Instance.UnsubscribeProfile(profileToRemove);
        ActiveChannels.Clear();
        ActiveInputChannels.Clear();
        profiles.Remove(profileToRemove);
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
                ProfileList = []
            };

            var newProfile = new Profile
            {

                Name = "DaqifiLastSessionProfile",
                ProfileId = Guid.NewGuid(),
                CreatedOn = DateTime.Now,
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
            if (obj is not Profile item)
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
}