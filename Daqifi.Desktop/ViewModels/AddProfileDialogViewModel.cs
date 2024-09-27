using Bugsnag.Payload;
using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Commands;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.Models;
using Daqifi.Desktop.View;
using GalaSoft.MvvmLight;
using NLog.Time;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace Daqifi.Desktop.ViewModels
{
    public class AddProfileDialogViewModel : ViewModelBase
    {
        #region Private Variables
        private string _profileName = "DaqifiProfile";
        private readonly IDialogService _dialogService;
        private int _selectedStreamingFrequency;
        #endregion

        #region Properties
        private readonly AppLogger AppLogger = AppLogger.Instance;


        private ObservableCollection<IStreamingDevice> _availableDevices = new ObservableCollection<IStreamingDevice>();
        public ObservableCollection<IStreamingDevice> AvailableDevices
        {
            get => _availableDevices;
            set
            {
                _availableDevices = value;

                RaisePropertyChanged();
            }
        }

        private ObservableCollection<IChannel> _availableChannels = new ObservableCollection<IChannel>();
        public ObservableCollection<IChannel> AvailableChannels
        {
            get => _availableChannels;
            set
            {
                _availableChannels = value;

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
        private bool _canAddProfile;

        public bool canAddProfile
        {
            get => _canAddProfile;
            set
            {
                _canAddProfile = value;
                RaisePropertyChanged();
            }
        }

        public int SelectedStreamingFrequency
        {
            get => _selectedStreamingFrequency;
            set
            {
                if (value < 1) { return; }
                ///checkAddProfileButton();
                _selectedStreamingFrequency = value;
                RaisePropertyChanged();
            }
        }
        #endregion

        #region Constructor
        public AddProfileDialogViewModel() : this(ServiceLocator.Resolve<IDialogService>()) { }

        public AddProfileDialogViewModel(IDialogService dialogService)
        {

            _dialogService = dialogService;
            foreach (var device in ConnectionManager.Instance.ConnectedDevices)
            {
                AvailableDevices.Add(device);
            }
            if (AvailableDevices.Count > 0)
            {
                foreach (var device in AvailableDevices)
                {
                    if (device != null)
                    {
                        GetAvailableChannels(device);
                    }
                }
            }

        }
        #endregion

        public void checkAddProfileButton()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(ProfileName) && ProfileName.Length != 0)
                {
                    canAddProfile = true;
                }
                else { canAddProfile = false; }

                if (SelectedStreamingFrequency > 1)
                {
                    canAddProfile = true;
                }
                else { canAddProfile = false; }

            }
            catch (System.Exception ex)
            {

                AppLogger.Error(ex, "Error in adding profile ");
            }
        }
        public void GetAvailableChannels(IStreamingDevice device)
        {
            try
            {
                foreach (var channel in device.DataChannels)
                {
                    if (!AvailableChannels.Any(x => x.Name == channel.Name))
                    {
                        AvailableChannels.Add(channel);
                    }
                }
            }
            catch (System.Exception ex)
            {
                AppLogger.Error(ex, "Error in getting Available Channels");
            }

        }


        #region Command Delegatges
        public ICommand AddProfileCommand => new DelegateCommand(OnSelectedProfileExecute, OnSelectedProfileCanExecute);

        private bool OnSelectedProfileCanExecute(object parameter)
        {
            if (parameter != null)
            {
                return true;
            }
            return false;
        }


        private void OnSelectedProfileExecute(object parameter)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ProfileName))
                {
                    return;
                }

                if (SelectedStreamingFrequency == 0)
                {
                    return;
                }

                var parameters = parameter as object[];
                if (parameters == null || parameters.Length < 2)
                {
                    return;
                }

                var selectedChannels = ((IEnumerable)parameters[0]).Cast<IChannel>().ToList();
                var selectedDevices = ((IEnumerable)parameters[1]).Cast<IStreamingDevice>().ToList();

                if (!selectedChannels.Any() || !selectedDevices.Any())
                {
                    return;
                }

                var addProfileModel = new AddProfileModel
                {
                    ProfileList = new List<Profile>()
                };

                var newProfile = new Profile
                {
                    Name = ProfileName,
                    ProfileId = Guid.NewGuid(),
                    CreatedOn = DateTime.Now,
                    Devices = new ObservableCollection<ProfileDevice>()
                };

                foreach (var selectedDevice in selectedDevices)
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
                            var isSelected = selectedChannels.Any(sc => sc.Name == dataChannel.Name);
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
            catch (System.Exception ex)
            {
                AppLogger.Error(ex, "Error in OnSelectedProfileExecute");
            }
        }
        #endregion
    }
}
