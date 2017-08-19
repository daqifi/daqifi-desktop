using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Commands;
using Daqifi.Desktop.Configuration;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.View;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Daqifi.Desktop.Bootloader;
using Daqifi.Desktop.Loggers;
using Microsoft.Win32;

namespace Daqifi.Desktop.ViewModels
{
    public class DaqifiViewModel : ObservableObject
    {
        #region Private Variables
        private bool _isBusy;
        private bool _isLoggedDataBusy;
        private bool _isDeviceSettingsOpen;
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
        private IDevice _selectedDevice;
        private IChannel _selectedChannel;
        private LoggingSession _selectedLoggingSession;
        private bool _isLogging;
        private bool _canToggleLogging;
        private string _loggedDataBusyReason;
        private string _firmwareFilePath;
        #endregion

        #region Properties
        public AppLogger AppLogger = AppLogger.Instance;

        public WindowState ViewWindowState
        {
            get { return _viewWindowState; }
            set 
            {
                _viewWindowState = value;
                NotifyPropertyChanged("FlyoutWidth");
                NotifyPropertyChanged("FlyoutHeight");
            }
        }

        /// <summary>
        /// Used to indicate that the program is GUI.  A modal progress ring will be presented to the user. 
        /// </summary>
        public bool IsBusy
        {
            get { return _isBusy; }
            private set
            {
                _isBusy = value;
                NotifyPropertyChanged("IsBusy");
            }
        }

        public bool IsLoggedDataBusy
        {
            get { return _isLoggedDataBusy; }
            private set
            {
                _isLoggedDataBusy = value;
                NotifyPropertyChanged("IsLoggedDataBusy");
            }
        }

        public bool IsLogging
        {
            get { return _isLogging; }
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
            get { return _canToggleLogging; }
            private set
            {
                _canToggleLogging = value;
                NotifyPropertyChanged("CanToggleLogging");
            }
        }

        public bool IsDeviceSettingsOpen
        {
            get { return _isDeviceSettingsOpen; }
            set
            {
                _isDeviceSettingsOpen = value;
                NotifyPropertyChanged("IsDeviceSettingsOpen");
            }
        }

        public bool IsLoggingSessionSettingsOpen
        {
            get { return _isLoggingSessionSettingsOpen; }
            set
            {
                _isLoggingSessionSettingsOpen = value;
                NotifyPropertyChanged("IsLoggingSessionSettingsOpen");
            }
        }

        public bool IsChannelSettingsOpen
        {
            get { return _isChannelSettingsOpen; }
            set
            {
                _isChannelSettingsOpen = value;
                NotifyPropertyChanged("IsChannelSettingsOpen");
            }
        }

        public bool IsLiveGraphSettingsOpen
        {
            get { return _isLiveGraphSettingsOpen; }
            set
            {
                _isLiveGraphSettingsOpen = value;
                NotifyPropertyChanged("IsLiveGraphSettingsOpen");
            }
        }

        public int Width
        {
            get { return _width; }
            set
            {
                _width = value;
                NotifyPropertyChanged("Width");
                NotifyPropertyChanged("FlyoutWidth");
            }
        }

        public int Height
        {
            get { return _height; }
            set
            {
                _height = value;
                NotifyPropertyChanged("Height");
                NotifyPropertyChanged("FlyoutHeight");
            }
        }

        public string FirmwareFilePath
        {
            get { return _firmwareFilePath; }
            set
            {
                _firmwareFilePath = value;
                NotifyPropertyChanged("FirmwareFilePath");
            }
        }

        public int SelectedIndex
        {
            get { return _selectedIndex; }
            set
            {
                _selectedIndex = value;
                CloseFlyouts();
                NotifyPropertyChanged("SelectedIndex");
            }
        }

        public int SelectedStreamingFrequency
        {
            get { return _selectedStreamingFrequency; }
            set
            {
                if (value < 1) return;
                
                if (LoggingManager.Instance.Active)
                {
                    var errorDialogViewModel = new ErrorDialogViewModel("Cannot change sampling frequency while logging.");
                    _dialogService.ShowDialog<ErrorDialog>(this, errorDialogViewModel);
                    return;
                }

                SelectedDevice.StreamingFrequency = value;
                _selectedStreamingFrequency = SelectedDevice.StreamingFrequency;
                NotifyPropertyChanged("SelectedStreamingFrequency");
            }
        }

        public int SelectedChannelOutput
        {
            get { return _selectedStreamingFrequency; }
            set
            {
                if (SelectedChannel.Direction != ChannelDirection.Output) return;

                _selectedChannelOutput = value;
                SelectedChannel.OutputValue = value;
            }
        }

        public double FlyoutWidth
        {
            get 
            {
               if(ViewWindowState== WindowState.Maximized)
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

        public IDevice SelectedDevice
        {
            get { return _selectedDevice; }
            set 
            { 
                _selectedDevice = value;
                NotifyPropertyChanged("SelectedDevice");
            }
        }

        public IChannel SelectedChannel
        {
            get { return _selectedChannel; }
            set
            {
                _selectedChannel = value;
                NotifyPropertyChanged("SelectedChannel");
            }
        }

        public LoggingSession SelectedLoggingSession
        {
            get { return _selectedLoggingSession; }
            set
            {
                _selectedLoggingSession = value;
                NotifyPropertyChanged("SelectedLoggingSession");
            }
        }

        public ObservableCollection<IDevice> ConnectedDevices { get; } = new ObservableCollection<IDevice>();
        public ObservableCollection<IChannel> ActiveChannels { get; } = new ObservableCollection<IChannel>();
        public ObservableCollection<IChannel> ActiveInputChannels { get; } = new ObservableCollection<IChannel>();
        public ObservableCollection<LoggingSession> LoggingSessions { get; } = new ObservableCollection<LoggingSession>();

        public PlotLogger Plotter { get; }
        public DatabaseLogger DbLogger { get; }

        public string LoggedDataBusyReason
        {
            get { return _loggedDataBusyReason; }
            set
            {
                _loggedDataBusyReason = value;
                NotifyPropertyChanged("LoggedDataBusyReason");
            }
        }
        #endregion

        #region Constructor
        public DaqifiViewModel() : this(ServiceLocator.Resolve<IDialogService>()){ }

        public DaqifiViewModel(IDialogService dialogService)
        {
            try
            {
                _dialogService = dialogService;

                RegisterCommands();

                //Manage connected device list
                ConnectionManager.Instance.PropertyChanged += UpdateUi;

                //Manage data for plotting
                LoggingManager.Instance.PropertyChanged += UpdateUi;
                Plotter = new PlotLogger();
                LoggingManager.Instance.AddLogger(Plotter);

                //Database logging
                DbLogger = new DatabaseLogger();
                LoggingManager.Instance.AddLogger(DbLogger);
                using (var context = new LoggingContext())
                {
                    var previouseSessions = new List<LoggingSession>();
                    var previouseSampleSessions = (from s in context.Sessions select s).ToList();
                    foreach (var session in previouseSampleSessions)
                    {
                        if (!previouseSessions.Contains(session)) previouseSessions.Add(session);
                    }
                    LoggingManager.Instance.LoggingSessions = previouseSessions;
                }

                //Configure Default Grid Lines
                Plotter.ShowingMinorXAxisGrid = false;
                Plotter.ShowingMinorYAxisGrid = false;

                //TODO do I need on closing events to close files that are currently open

            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error(ex, "DAQifiViewModel");
            }
          
        }

        public void RegisterCommands()
        {
            ShowConnectionDialogCommand = new DelegateCommand(ShowConnectionDialog, CanShowConnectionDialog);
            ShowAddChannelDialogCommand = new DelegateCommand(ShowAddChannelDialog, CanShowAddChannelDialog);
            ShowSelectColorDialogCommand = new DelegateCommand(ShowSelectColorDialog, CanShowSelectColorDialogCommand);
            RemoveChannelCommand = new DelegateCommand(RemoveChannel, CanRemoveChannelCommand);
            OpenLiveGraphSettingsCommand = new DelegateCommand(OpenLiveGraphSettings, CanOpenLiveGraphSettings);
            OpenDeviceSettingsCommand = new DelegateCommand(OpenDeviceSettings, CanOpenDeviceSettings);
            OpenChannelSettingsCommand = new DelegateCommand(OpenChannelSettings, CanOpenChannelSettings);
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
            UploadFirmwareCommand = new DelegateCommand(UpdateFirmware, CanUploadFirmware);

            HostCommands.ShutdownCommand.RegisterCommand(ShutdownCommand);
        }
        #endregion
        
        #region Command Properties
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
                var errorDialogViewModel = new ErrorDialogViewModel("Cannot disconnect device while logging.");
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

        public ICommand UploadFirmwareCommand { get; private set; }
        private bool CanUploadFirmware(object o)
        {
            return true;
        }
        #endregion

        #region Command Methods
        private void ShowConnectionDialog(object o)
        {
            var connectionDialogViewModel = new ConnectionDialogViewModel();
            connectionDialogViewModel.StartConnectionFinders();
            _dialogService.ShowDialog<ConnectionDialog>(this, connectionDialogViewModel);
        }

        private void ShowAddChannelDialog(object o)
        {
            var addChannelDialogViewModel = new AddChannelDialogViewModel();
            _dialogService.ShowDialog<AddChannelDialog>(this, addChannelDialogViewModel);
        }

        private void ShowSelectColorDialog(object o)
        {
            var item = o as IColorable;
            if (item == null) AppLogger.Error("Cannot set the color of an item that does not implement IHasColor.");

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
                   if (channel==channelToRemove)
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
            var deviceToRemove = o as IDevice;
            foreach(var channel in deviceToRemove.DataChannels)
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
            foreach(var device in ConnectedDevices)
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
            var openFileDialog = new OpenFileDialog {Filter = "Firmware Files (*.hex)|*.hex"};
            if (openFileDialog.ShowDialog() == true)
            {
                FirmwareFilePath = openFileDialog.FileName;
            }
        }

        public void UpdateFirmware(object o)
        {
            var bootloader = new Pic32Bootloader();
            bootloader.RequestVersion();
            //bootloader.LoadFirmware(FirmwareFilePath);
        }

        private void OpenLiveGraphSettings(object o)
        {
            CloseFlyouts();
            IsLiveGraphSettingsOpen = true;
        }

        private void OpenDeviceSettings(object o)
        {
            var item = o as IDevice;
            if (item == null) AppLogger.Error("Error opening device settings");

            CloseFlyouts();
            SelectedDevice = item;
            IsDeviceSettingsOpen = true;
        }

        private void OpenChannelSettings(object o)
        {
            var item = o as IChannel;
            if (item == null)
            {
                AppLogger.Error("Error opening channel settings");
                return;
            } 

            CloseFlyouts();
            SelectedChannel = item;
            IsChannelSettingsOpen = true;
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
            var session = o as LoggingSession;
            if (session == null)
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
            var session = o as LoggingSession;
            if (session == null)
            {
                AppLogger.Error("Error exporting logging session");
                return;
            }

            var exportDialogViewModel = new ExportDialogViewModel(session);
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
                var session = o as LoggingSession;

                if (session == null)
                {
                    AppLogger.Error("Error deleting logging session");
                    return;
                }

                var result = await ShowMessage("Delete Confirmation", "Are you sure you want to delete " + session.Name +"?" , MessageDialogStyle.AffirmativeAndNegative).ConfigureAwait(false);
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
                        NotifyPropertyChanged("LoggingSessions");
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
                        while(LoggingSessions.Count > 0)
                        {
                            var session = LoggingSessions.ElementAt(0);
                            DbLogger.DeleteLoggingSession(session);
                            Application.Current.Dispatcher.Invoke(delegate
                            {
                                LoggingSessions.Remove(session);
                            });
                        }
                        NotifyPropertyChanged("LoggingSessions");
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
            var deviceToReboot = o as IDevice;

            if(deviceToReboot == null) return;

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

        public void UpdateUi(object sender, PropertyChangedEventArgs args)
        {
            switch (args.PropertyName)
            {
                case "ConnectedDevices":
                    ConnectedDevices.Clear();
                    foreach (var connectedDevice in ConnectionManager.Instance.ConnectedDevices)
                    {
                        ConnectedDevices.Add(connectedDevice);
                    }
                    break;
                case "SubscribedChannels":
                    ActiveChannels.Clear();
                    ActiveInputChannels.Clear();
                    foreach (var channel in LoggingManager.Instance.SubscribedChannels)
                    {
                        if(!channel.IsOutput)
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
            IsDeviceSettingsOpen = false;
            IsChannelSettingsOpen = false;
            IsLoggingSessionSettingsOpen = false;
            IsLiveGraphSettingsOpen = false;
        }
    }
}