using Daqifi.Desktop.Bootloader;
using Daqifi.Desktop.Channel;
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
using CommunityToolkit.Mvvm.Input;

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
    private Pic32Bootloader _bootloader;
    [ObservableProperty]
    private string _version;
    [ObservableProperty]
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
    private bool _selectedDeviceSupportsFirmwareUpdate;
    private HidDeviceFinder _hidDeviceFinder;
    [ObservableProperty]
    private bool _hasNoHidDevices = true;
    private ConnectionDialogViewModel _connectionDialogViewModel;
    private readonly IDbContextFactory<LoggingContext> _loggingContext;
    private string _selectedLoggingMode = "Stream to App";
    private bool _isLogToDeviceMode;
    // Add a field to store the device being updated during firmware update process
    private IStreamingDevice? _deviceBeingUpdated;
    #endregion

    #region Properties

    public ObservableCollection<HidFirmwareDevice> AvailableHidDevices { get; } = [];
    public ObservableCollection<IStreamingDevice> ConnectedDevices { get; } = [];
    public ObservableCollection<Profile> profiles { get; } = LoggingManager.Instance.SubscribedProfiles;

    public ObservableCollection<Notifications> NotificationList { get; } = [];
    public ObservableCollection<IChannel> ActiveChannels { get; } = [];
    public ObservableCollection<IChannel> ActiveInputChannels { get; } = [];
    public ObservableCollection<LoggingSession> LoggingSessions => LoggingManager.Instance.LoggingSessions;

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

    // Re-add properties for manually instantiated commands
    public ICommand DeleteLoggingSessionCommand { get; private set; }
    public ICommand DeleteAllLoggingSessionCommand { get; private set; }
    public ICommand ToggleChannelVisibilityCommand { get; private set; }
    public ICommand ToggleLoggedSeriesVisibilityCommand { get; private set; }
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

            // Use the stored device reference instead of SelectedDevice (which might be null)
            if (_deviceBeingUpdated is not IFirmwareUpdateDevice updateDevice)
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
        
        // Clear the device reference after update completion
        _deviceBeingUpdated = null;
    }
    private void HandleFirmwareUploadCompleted(object sender, RunWorkerCompletedEventArgs e)
    {
        if (e.Error != null || e.Cancelled)
        {
            IsFirmwareUploading = false;
            _deviceBeingUpdated = null; // Clear reference on error
            return;
        }
            
        var isManualUpload = (bool)e.Result;
        
        if (isManualUpload)
        {
            IsUploadComplete = true;
            ShowUploadSuccessMessage();
            _deviceBeingUpdated = null; // Clear reference after manual upload
        }
        else
        {
            InitializeUpdateWiFiBackgroundWorker();
        }
        IsFirmwareUploading = false;
    }

    [RelayCommand]
    private void UploadFirmware()
    {
        if (SelectedDevice is not SerialStreamingDevice serialStreamingDevice)
        {
            return;
        }
        
        SelectedDeviceSupportsFirmwareUpdate = true;
        
        // Store a reference to the device being updated
        _deviceBeingUpdated = SelectedDevice;
        
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
        
        serialStreamingDevice.ForceBootloader();
        serialStreamingDevice.Disconnect();
        
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
    private void ShowDAQifiSettingsDialog()
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

        SelectedDeviceSupportsFirmwareUpdate = device is SerialStreamingDevice;
        
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
                bool deleteSucceeded = false;
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
        catch (System.Exception ex)
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
                var successfullyDeletedSessions = new List<LoggingSession>();

                foreach(var session in sessionsToDelete)
                {
                    try
                    {
                        DbLogger.DeleteLoggingSession(session);
                        successfullyDeletedSessions.Add(session);
                    }
                    catch (Exception dbEx)
                    {
                        _appLogger.Error(dbEx, $"Failed to delete session {session.ID} from database during delete all.");
                    }
                }

                if (successfullyDeletedSessions.Any())
                {
                    Application.Current.Dispatcher.Invoke(delegate
                    {
                        foreach(var sessionToRemove in successfullyDeletedSessions)
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

    private string latestFirmwareVersion;
    [RelayCommand]
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
        if (deviceToRemove?.DeviceSerialNo == null)
        {
            return;
        }

        var notificationsToRemove = NotificationList
            .FirstOrDefault(x => x.DeviceSerialNo != null && x.DeviceSerialNo == deviceToRemove.DeviceSerialNo && x.isFirmwareUpdate);

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
        if (profiles.Any(x => x.IsProfileActive))
        {
            var errorDialogViewModel = new ErrorDialogViewModel("Cannot remove profile while profile is active");
            _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
            return;
        }
        LoggingManager.Instance.UnsubscribeProfile(profile);
        ActiveChannels.Clear();
        ActiveInputChannels.Clear();
        profiles.Remove(profile);
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

    [RelayCommand]
    public void GetAvailableChannels(IStreamingDevice device)
    {
        AvailableChannels.Clear();

        foreach (var channel in device.DataChannels)
        {
            AvailableChannels.Add(channel);
        }
    }

    [RelayCommand]
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
            var anyActiveProfile = profiles.FirstOrDefault(x => x.IsProfileActive);
            if (anyActiveProfile != null && anyActiveProfile.ProfileId != SelectedProfile.ProfileId)
            {
                var errorDialogViewModel = new ErrorDialogViewModel("Multiple Profiles Cannot be Active.");
                _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
                _appLogger.Error("Multiple Profiles Cannot be Active.");
                return;
            }

            var connectedDevices = ConnectedDevices
                .Where(cd => SelectedProfile.Devices.Any(id => id.DeviceSerialNo == cd.DeviceSerialNo))
                .ToList();

            // Block only if no devices are connected
            if (connectedDevices == null || connectedDevices.Count == 0)
            {
                var errorDialogViewModel = new ErrorDialogViewModel("Profile cannot be active. No connected devices.");
                _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
                return;
            }

            // Warn if some devices are missing
            var missingDevices = SelectedProfile.Devices
                .Where(pd => !ConnectedDevices.Any(cd => cd.DeviceSerialNo == pd.DeviceSerialNo))
                .ToList();
            if (missingDevices.Count > 0)
            {
                var warningMsg = $"Warning: The following devices from the profile are not currently connected: {string.Join(", ", missingDevices.Select(d => d.DeviceName))}. Profile will be loaded with available devices.";
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

            // Iterate through connected devices
            foreach (var connectedDevice in connectedDevices)
            {
                SelectedDevice = connectedDevice;
                UpdateProfileSelectedDevice = SelectedDevice;

                // Update device frequencies
                var matchingDevice = SelectedProfile.Devices.FirstOrDefault(device => device.DeviceSerialNo == connectedDevice.DeviceSerialNo);
                if (matchingDevice != null)
                {
                    SelectedStreamingFrequency = matchingDevice.SamplingFrequency;
                    connectedDevice.StreamingFrequency = matchingDevice.SamplingFrequency;
                }

                // Handle profile activation or deactivation
                foreach (var device in SelectedProfile.Devices)
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
                            if (SelectedProfile.IsProfileActive)
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
            SelectedProfile.IsProfileActive = !SelectedProfile.IsProfileActive;
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

            // Subscribe to debug events if this is a streaming device
            if (connectedDevice is AbstractStreamingDevice streamingDevice)
            {
                streamingDevice.DebugDataReceived += OnDebugDataReceived;
                streamingDevice.SetDebugMode(IsDebugModeEnabled);
            }
        }
    }
    public async void UpdateUi(object sender, PropertyChangedEventArgs args)
    {

        switch (args.PropertyName)
        {
            case "SubscribedProfiles":

                if (LoggingManager.Instance.SubscribedProfiles.Count == 0)
                { profiles.Clear(); }
                foreach (var connectedProfiles in LoggingManager.Instance.SubscribedProfiles)
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
        var debugWindow = new Daqifi.Desktop.View.DebugWindow(this);
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
}