using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Commands;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.DialogService;
using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ToolTip;

namespace Daqifi.Desktop.ViewModels
{
    public class AddProfileConfirmationDialogViewModel : ViewModelBase
    {
        #region Private Variables
        private IStreamingDevice _selectedDevice;
        private string _profileName="Hello";
        private IDialogService _dialogService;
        private int _selectedStreamingFrequency;
        #endregion

        #region Properties
        private readonly  AppLogger AppLogger = AppLogger.Instance;

        private readonly DaqifiViewModel _daqifiViewModel;
        public ObservableCollection<IStreamingDevice> AvailableDevices { get; } = new ObservableCollection<IStreamingDevice>();
        public ObservableCollection<IChannel> AvailableChannels { get; } = new ObservableCollection<IChannel>();


        public IStreamingDevice SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                _selectedDevice = value;
                RaisePropertyChanged();
            }
        }


        private Visibility _saveProfileExisting = Visibility.Collapsed;
        public Visibility SaveProfileExisting
        {
            get => _saveProfileExisting;
            set
            {
                _saveProfileExisting = value;
                RaisePropertyChanged();
            }
        }

      


        public string ProfileName
        {
            get => _profileName;
            set
            {
                _profileName = value;
                RaisePropertyChanged();
            }
        }


        public int SelectedStreamingFrequency
        {
            get => _selectedStreamingFrequency;
            set
            {
                if (value < 1) { return; }
                _selectedStreamingFrequency = value;
                RaisePropertyChanged();
            }
        }


        #endregion


        #region Constructor
        public AddProfileConfirmationDialogViewModel() : this(ServiceLocator.Resolve<IDialogService>()) { }

        public AddProfileConfirmationDialogViewModel(IDialogService dialogService)
        {

            _dialogService = dialogService;
            if (_dialogService != null && _dialogService.Views.Count > 0 && _dialogService.Views[0] is Daqifi.Desktop.MainWindow mainWindow)
            {
                if (mainWindow.DataContext is DaqifiViewModel daqifiViewModel)
                {

                    _daqifiViewModel = daqifiViewModel;
                    if (_daqifiViewModel != null)
                    {
                        if (_daqifiViewModel.ActiveChannels != null && _daqifiViewModel.ActiveChannels.Count > 0) { SaveProfileExisting = Visibility.Visible; }
                        else { SaveProfileExisting = Visibility.Collapsed; }
                    }
                }
            }
        }
        #endregion
        #region Command Delegatges
        public ICommand AddNewProfileCommand => new DelegateCommand(AddNewProfileExecute, OnSelectedProfileCanExecute);
        public ICommand ExistingProfileCommand => new DelegateCommand(SaveExistingProfileExecute, OnSelectedProfileCanExecute);

        private bool OnSelectedProfileCanExecute(object selectedItems)
        {
            return true;
        }

        private void AddNewProfileExecute(object selectedItems)
        {
            _daqifiViewModel.ShowAddProfileDialog(null);

        }
        private void SaveExistingProfileExecute(object selectedItems)
        {

            _daqifiViewModel.SaveExistingSetting();
        }

        #endregion


    }
}
