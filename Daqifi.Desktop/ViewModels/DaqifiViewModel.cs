using Daqifi.Desktop.Bootloader;
using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Commands;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Configuration;
using Daqifi.Desktop.DataModel.Channel;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Device.HidDevice;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.View;
using GalaSoft.MvvmLight;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Collections;
using Daqifi.Desktop.Device.SerialDevice;
using System.Threading;
using Daqifi.Desktop.Models;
using System.Web.Profile;
using System.Drawing;
using Daqifi.Desktop.UpdateVersion;
using static System.Net.WebRequestMethods;
using File = System.IO.File;

namespace Daqifi.Desktop.ViewModels
{
    public class DaqifiViewModel : ViewModelBase
    {
        #region Private Variables
        private bool _isBusy;
        private bool _isLoggedDataBusy;
        private bool _isDeviceSettingsOpen;
        private bool _isProfileSettingsOpen;
        private bool _isNotificationsOpen;
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
        private int _selectedChannelOutput;
        public WindowState _viewWindowState;
        private readonly IDialogService _dialogService;
        private IStreamingDevice _selectedDevice;
        private VersionNotification _versionNotification;
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
        private HidDeviceFinder _hidDeviceFinder;
        private bool _hasNoHidDevices = true;
        private ConnectionDialogViewModel _connectionDialogViewModel;
        #endregion

        #region Properties

        public ObservableCollection<HidFirmwareDevice> AvailableHidDevices { get; } = new ObservableCollection<HidFirmwareDevice>();
        public ObservableCollection<IStreamingDevice> ConnectedDevices { get; } = new ObservableCollection<IStreamingDevice>();
        public ObservableCollection<Profile> profiles { get; } = new ObservableCollection<Profile>();

        public ObservableCollection<Notifications> notificationlist { get; } = new ObservableCollection<Notifications>();
        public ObservableCollection<IChannel> ActiveChannels { get; } = new ObservableCollection<IChannel>();
        public ObservableCollection<IChannel> ActiveInputChannels { get; } = new ObservableCollection<IChannel>();
        public ObservableCollection<LoggingSession> LoggingSessions { get; } = new ObservableCollection<LoggingSession>();

        public PlotLogger Plotter { get; }
        public DatabaseLogger DbLogger { get; }
        public SummaryLogger SummaryLogger { get; }
        public ObservableCollection<IStreamingDevice> AvailableDevices { get; } = new ObservableCollection<IStreamingDevice>();
        public ObservableCollection<IChannel> AvailableChannels { get; } = new ObservableCollection<IChannel>();
        public string Version
        {
            get => _version;
            set
            {
                _version = value;
                RaisePropertyChanged();
            }
        }

        public string FirmwareFilePath
        {
            get => _firmwareFilePath;
            set
            {
                _firmwareFilePath = value;
                RaisePropertyChanged();
            }
        }

        public bool IsFirmwareUploading
        {
            get => _isFirmwareUploading;
            set
            {
                _isFirmwareUploading = value;
                RaisePropertyChanged();
            }
        }

        public bool IsUploadComplete
        {
            get => _isUploadComplete;
            set
            {
                _isUploadComplete = value;
                RaisePropertyChanged();
            }
        }

        public bool HasErrorOccured
        {
            get => _hasErrorOccured;
            set
            {
                _hasErrorOccured = value;
                RaisePropertyChanged();
            }
        }

        public int UploadFirmwareProgress
        {
            get => _uploadFirmwareProgress;
            set
            {
                _uploadFirmwareProgress = value;
                RaisePropertyChanged();
                RaisePropertyChanged("UploadFirmwareProgressText");
            }
        }

        public string UploadFirmwareProgressText => ($"Upload Progress: {UploadFirmwareProgress}%");
        public bool HasNoHidDevices
        {
            get => _hasNoHidDevices;
            set
            {
                _hasNoHidDevices = value;
                RaisePropertyChanged();
            }
        }
        public IStreamingDevice UpdateProfileSelectedDevice
        {
            get => _updateProfileSelectedDevice;
            set
            {
                _updateProfileSelectedDevice = value;
                GetAvailableChannels(_updateProfileSelectedDevice);
                RaisePropertyChanged();
            }
        }
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                _isBusy = value;
                RaisePropertyChanged();
            }
        }

        public bool IsLoggedDataBusy
        {
            get => _isLoggedDataBusy;
            private set
            {
                _isLoggedDataBusy = value;
                RaisePropertyChanged();
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
                    Plotter.ClearPlot();
                    foreach (var device in ConnectedDevices)
                    {
                        device.InitializeStreaming();
                    }
                }
                else
                {
                    foreach (var device in ConnectedDevices)
                    {
                        device.StopStreaming();
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
                RaisePropertyChanged();
            }
        }

        public bool IsDeviceSettingsOpen
        {
            get => _isDeviceSettingsOpen;
            set
            {
                _isDeviceSettingsOpen = value;
                RaisePropertyChanged();
            }
        }
        public bool IsProfileSettingsOpen
        {
            get => _isProfileSettingsOpen;
            set
            {
                _isProfileSettingsOpen = value;
                RaisePropertyChanged();
            }
        }
        public bool IsNotificationsOpen
        {
            get => _isNotificationsOpen;
            set
            {
                _isNotificationsOpen = value;
                RaisePropertyChanged();
            }
        }

        public bool IsLoggingSessionSettingsOpen
        {
            get => _isLoggingSessionSettingsOpen;
            set
            {
                _isLoggingSessionSettingsOpen = value;
                RaisePropertyChanged();
            }
        }

        public bool IsLogSummaryOpen
        {
            get => _isLogSummaryOpen;
            set
            {
                _isLogSummaryOpen = value;
                RaisePropertyChanged();
            }
        }

        public bool IsChannelSettingsOpen
        {
            get => _isChannelSettingsOpen;
            set
            {
                _isChannelSettingsOpen = value;
                RaisePropertyChanged();
            }
        }


        private int _notificationCount;

        public int NotificationCount
        {
            get => _notificationCount;
            set
            {
                _notificationCount = value;
                RaisePropertyChanged(nameof(NotificationCount));
            }
        }


        private string _versionName;
        public string VersionName
        {
            get => _versionName;
            set
            {
                _versionName = value;
                RaisePropertyChanged(nameof(VersionName));
            }
        }

        public bool IsLiveGraphSettingsOpen
        {
            get => _isLiveGraphSettingsOpen;
            set
            {
                _isLiveGraphSettingsOpen = value;
                RaisePropertyChanged();
            }
        }

        public int Width
        {
            get => _width;
            set
            {
                _width = value;
                RaisePropertyChanged();
                RaisePropertyChanged("FlyoutWidth");
            }
        }

        public int Height
        {
            get => _height;
            set
            {
                _height = value;
                RaisePropertyChanged();
                RaisePropertyChanged("FlyoutHeight");
            }
        }

        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                _selectedIndex = value;
                CloseFlyouts();
                RaisePropertyChanged();
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
                RaisePropertyChanged();
            }
        }

        public int SelectedChannelOutput
        {
            get => _selectedStreamingFrequency;
            set
            {
                if (SelectedChannel.Direction != ChannelDirection.Output) { return; }

                _selectedChannelOutput = value;
                SelectedChannel.OutputValue = value;
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
                RaisePropertyChanged();
            }
        }

        public IChannel SelectedChannel
        {
            get => _selectedChannel;
            set
            {
                _selectedChannel = value;
                RaisePropertyChanged();
            }
        }
        public Profile SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                _selectedProfile = value;
                RaisePropertyChanged();

            }
        }



        public LoggingSession SelectedLoggingSession
        {
            get => _selectedLoggingSession;
            set
            {
                _selectedLoggingSession = value;
                RaisePropertyChanged();
            }
        }



        public string LoggedDataBusyReason
        {
            get => _loggedDataBusyReason;
            set
            {
                _loggedDataBusyReason = value;
                RaisePropertyChanged();
            }
        }
        private readonly AppLogger AppLogger = AppLogger.Instance;

        public WindowState ViewWindowState
        {
            get => _viewWindowState;
            set
            {
                _viewWindowState = value;
                RaisePropertyChanged("FlyoutWidth");
                RaisePropertyChanged("FlyoutHeight");
            }
        }
        #endregion

        #region Constructor
        public DaqifiViewModel() : this(ServiceLocator.Resolve<IDialogService>()) { }

        public DaqifiViewModel(IDialogService dialogService)
        {
            try
            {
                _dialogService = dialogService;

                RegisterCommands();

                // Manage connected streamingDevice list
                ConnectionManager.Instance.PropertyChanged += UpdateUi;

                // Manage data for plotting
                LoggingManager.Instance.PropertyChanged += UpdateUi;
                Plotter = new PlotLogger();
                LoggingManager.Instance.AddLogger(Plotter);

                // Database logging
                DbLogger = new DatabaseLogger();
                LoggingManager.Instance.AddLogger(DbLogger);

                //Xml profiles load

                LoggingManager.Instance.AddAndRemoveProfileXml(null, false);
                ObservableCollection<Daqifi.Desktop.Models.Profile> observableProfileList = new ObservableCollection<Daqifi.Desktop.Models.Profile>(LoggingManager.Instance.LoadProfilesFromXml());
                //  Notifications 

                _versionNotification = new VersionNotification();
                LoggingManager.Instance.CheckApplicationVersion(_versionNotification);

                GetUpdateProfileAvailableDevice();

                // Summary Logger
                SummaryLogger = new SummaryLogger();
                LoggingManager.Instance.AddLogger(SummaryLogger);

                using (var context = new LoggingContext())
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


            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "DAQifiViewModel");
            }
        }

        private void OnHidDevicePropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Version")
            {
                Version = _bootloader.Version;
            }
        }

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
            HostCommands.ShutdownCommand.RegisterCommand(ShutdownCommand);
        }


        #endregion

        #region Command Properties
        public ICommand UploadFirmwareCommand { get; set; }
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

        void UploadFirmwareProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            UploadFirmwareProgress = e.ProgressPercentage;
        }

        private void HandleUploadCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            IsFirmwareUploading = false;
            if (e.Error != null)
            {
                AppLogger.Instance.Error(e.Error, "Problem Uploading Firmware");
                HasErrorOccured = true;
            }
            else
            {
                IsUploadComplete = true;
            }
        }

        #endregion

        #region Command Methods
        private bool CanUploadFirmware(object o)
        {
            return true;
        }

        public void UploadFirmware(object o)
        {
            // Check if the available port is opened:
            ObservableCollection<SerialStreamingDevice> _availableSerialDevices = _connectionDialogViewModel.AvailableSerialDevices;
            SerialStreamingDevice autodaqifiport = _availableSerialDevices.FirstOrDefault();
            SerialStreamingDevice manualserialdevice = _connectionDialogViewModel.ManualSerialDevice;

            SerialStreamingDevice port = manualserialdevice;
            // Check if serial ports auto/manual are not null
            if (port == null) { port = autodaqifiport; }
            if (port != null)
            {
                // Send the Daqifi command "Force Boot"
                string command = "SYSTem:FORceBoot\r\n";

                if (port.Write(command))
                {
                    // Once the Daqifi resets, the COM serial port is closed,
                    // and the HID port for managing the bootloader must be found
                    StartConnectionFinders();

                    // Update the variable 'HasNoHidDevices' in a backbround task
                    var bw2 = new BackgroundWorker();
                    bw2.DoWork += delegate
                    {
                        while (HasNoHidDevices == true)
                        {
                            Thread.Sleep(2000);
                            if (HasNoHidDevices == false)
                            {
                                // Connect HID if it was found before              
                                HidFirmwareDevice hidFirmwareDevice = ConnectHid(AvailableHidDevices);
                                if (hidFirmwareDevice != null)
                                {
                                    _bootloader = new Pic32Bootloader(hidFirmwareDevice.Device);
                                    _bootloader.PropertyChanged += OnHidDevicePropertyChanged;
                                    _bootloader.RequestVersion();

                                    var bw = new BackgroundWorker();
                                    bw.DoWork += delegate
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

                                    };
                                    bw.WorkerReportsProgress = true;
                                    bw.ProgressChanged += UploadFirmwareProgressChanged;
                                    bw.RunWorkerCompleted += HandleUploadCompleted;
                                    bw.RunWorkerAsync();
                                }
                            }
                        }
                    };
                    bw2.RunWorkerAsync();
                }
                else
                {
                    string msg = "Error writing to COM port";
                    AppLogger.Error(msg);
                }
            }
            else
            {
                string msg = "Error serial COM port detection";
                AppLogger.Error(msg);
            }
        }
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
            if (item == null) { AppLogger.Error("Cannot set the color of an item that does not implement IHasColor."); }

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

            foreach (var device in ConnectionManager.Instance.ConnectedDevices)
            {
                foreach (var channel in device.DataChannels)
                {
                    if (channel == channelToRemove)
                    {
                        LoggingManager.Instance.Unsubscribe(channel);
                        device.RemoveChannel(channel);
                        return;
                    }
                }
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
        }
        public void BrowseForFirmware(object o)
        {
            var openFileDialog = new OpenFileDialog { Filter = "Firmware Files (*.hex)|*.hex" };
            if (openFileDialog.ShowDialog() == true)
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
            if (item == null) { AppLogger.Error("Error opening streamingDevice settings"); }

            CloseFlyouts();
            SelectedDevice = item;
            IsDeviceSettingsOpen = true;
        }
        private void OpenChannelSettings(object o)
        {
            if (!(o is IChannel item))
            {
                AppLogger.Error("Error opening channel settings");
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
            if (item == null) AppLogger.Error("Error opening logging session settings");

            CloseFlyouts();
            SelectedLoggingSession = item;
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
                AppLogger.Error("Error exporting logging session");
                return;
            }

            var exportDialogViewModel = new ExportDialogViewModel(session.ID);
            _dialogService.ShowDialog<ExportDialog>(this, exportDialogViewModel);
        }
        private void ExportAllLoggingSession(object o)
        {
            if (LoggingSessions == null)
            {
                AppLogger.Error("Error exporting all logging sessions");
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
                    AppLogger.Error("Error deleting logging session");
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
                        RaisePropertyChanged("LoggingSessions");
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
                AppLogger.Error(ex, "Error Deleting Logging Session");
            }
        }
        private async void DeleteAllLoggingSession(object o)
        {
            try
            {
                if (LoggingSessions.Count == 0)
                {
                    AppLogger.Error("Error deleting logging session");
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
                        RaisePropertyChanged("LoggingSessions");
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
                AppLogger.Error(ex, "Error Deleting All Logging Session");
            }
        }
        private void RebootSelectedDevice(object o)
        {
            if (!(o is IStreamingDevice deviceToReboot)) return;

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
            System.Diagnostics.Process.Start("https://www.daqifi.com/support");
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
        public void UpdateUi(object sender, PropertyChangedEventArgs args)
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
                    foreach (var connectedDevice in ConnectionManager.Instance.ConnectedDevices)
                    {
                        ConnectedDevices.Add(connectedDevice);
                    }
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
                            Message = $"Please update latest application version:  {VersionName}",
                            Link = "https://github.com/daqifi/daqifi-desktop/releases"
                        };
                        if (!notificationlist.Any(n => n.Message == notify.Message || n.Link == notify.Link))
                        {
                            notificationlist.Add(notify);
                        }
                    }
                    break;
            }
            CanToggleLogging = ActiveChannels.Count > 0;
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
            IsNotificationsOpen=false;
        }

        #region update profile methods
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
                AppLogger.Error(ex, "Error opening add profile dialog");
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
                AppLogger.Error(ex, "Error opening confirmation dialog");
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
            profiles.Remove(ProfileToRemove);
            return;
        }
        private void OpenNotifications(object o)
        {
            IsNotificationsOpen = true;
        }
        /// <summary>
        /// Open profile settings flyout 
        /// </summary>
        /// <param name="obj"></param>
        private void OpenProfileSettings(object obj)
        {
            if (!(obj is Profile item))
            {
                AppLogger.Error("Error opening channel settings");
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
                foreach (var selectedchannel in selectedDevice.Channels)
                {
                    if (selectedchannel.IsChannelActive && !LoggingManager.Instance.SelectedProfileChannels.Any(x => x.Name == selectedchannel.Name))
                    {
                        LoggingManager.Instance.SelectedProfileChannels.Add(selectedchannel);
                    }
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
                    if (selectedDevice != null && selectedDevice.DataChannels.Count > 0)
                    {
                        var device = new ProfileDevice
                        {
                            MACAddress = selectedDevice.MacAddress,
                            DeviceName = selectedDevice.Name,
                            DevicePartName = selectedDevice.DevicePartNumber,
                            DeviceSerialNo = selectedDevice.DeviceSerialNo,
                            SamplingFrequency = SelectedStreamingFrequency,
                            Channels = new List<ProfileChannel>()
                        };

                        foreach (var dataChannel in selectedDevice.DataChannels)
                        {
                            var isSelected = ActiveChannels.Any(sc => sc.Name == dataChannel.Name);
                            var profileChannel = new ProfileChannel
                            {
                                Name = dataChannel.Name,
                                Type = dataChannel.TypeString.ToString(),
                                IsChannelActive = isSelected
                            };

                            device.Channels.Add(profileChannel);
                        }
                        newProfile.Devices.Add(device);
                    }
                }
                addProfileModel.ProfileList.Add(newProfile);
                LoggingManager.Instance.SubscribeProfile(newProfile);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Error opening confirmation dialog");
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
                    var errorDialogViewModel = new ErrorDialogViewModel("Error Activating Profile .");
                    _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
                    AppLogger.Error("Error Activating Profile");
                    return;
                }
                var anyActiveProfile = profiles.FirstOrDefault(x => x.IsProfileActive);
                if (anyActiveProfile != null)
                {
                    if (anyActiveProfile.ProfileId != item.ProfileId)
                    {
                        var errorDialogViewModel = new ErrorDialogViewModel("Multiple Profiles Cannot be Active .");
                        _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
                        AppLogger.Error("Multiple Profiles Cannot be Active .");
                        return;
                    }
                }
               
                if (profiles != null)
                {
                    var connectedDevices = ConnectedDevices.Where(connectedDevice => item.Devices.Any(itemDevice => itemDevice.DeviceSerialNo == connectedDevice.DeviceSerialNo)).ToList();
                    if (connectedDevices == null || connectedDevices.Count == 0)
                    {
                        var errorDialogViewModel = new ErrorDialogViewModel("profile connot be active , there is no connected devices in it.");
                        _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
                        return;
                    }
                    if (connectedDevices != null)
                    {
                        if (LoggingManager.Instance.Active)
                        {
                            var errorDialogViewModel = new ErrorDialogViewModel(" profile cannot be active while logging.");
                            _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
                            return;
                        }
                        else
                        {
                            foreach (var connectedDevice in connectedDevices)
                            {
                                SelectedDevice = connectedDevice;
                                UpdateProfileSelectedDevice = SelectedDevice;
                                SelectedStreamingFrequency = item.Devices[0].SamplingFrequency;
                                connectedDevice.StreamingFrequency = item.Devices.Where(device => device.DeviceSerialNo == connectedDevice.DeviceSerialNo).FirstOrDefault().SamplingFrequency;
                                if (item.IsProfileActive)
                                {

                                    foreach (var channel in item.Devices[0].Channels)
                                    {
                                        var profileChannel = AvailableChannels.Where(x => x.Name.ToString() == channel.Name.Trim() && x.TypeString.ToString() == channel.Type.Trim() && channel.IsChannelActive).FirstOrDefault();
                                        if (profileChannel != null)
                                        {
                                            LoggingManager.Instance.Unsubscribe(profileChannel);
                                        }
                                    }
                                    item.IsProfileActive = false;

                                }
                                else
                                {

                                    foreach (var channel in item.Devices[0].Channels)
                                    {
                                        var profileChannel = AvailableChannels.Where(x => x.Name.ToString() == channel.Name.Trim() && x.TypeString.ToString() == channel.Type.Trim() && channel.IsChannelActive).FirstOrDefault();
                                        if (profileChannel != null)
                                        {
                                            LoggingManager.Instance.Subscribe(profileChannel);
                                        }
                                    }
                                    item.IsProfileActive = true;

                                }
                            }


                        }
                    }
                    else
                    {
                        var errorDialogViewModel = new ErrorDialogViewModel("No connected device for selected profile");
                        _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
                        return;
                    }

                }
            }
            catch (Exception ex)
            {

                AppLogger.Error("Error activating Profile ");
            }


        }

        #endregion

        #endregion

    }
}
