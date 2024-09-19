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
        private IStreamingDevice _selectedDevice;
        private string _profileName = "Daqifi_profile";
        private readonly IDialogService _dialogService;
        private int _selectedStreamingFrequency;
        #endregion

        #region Properties
        public AppLogger AppLogger = AppLogger.Instance;
        public ObservableCollection<IStreamingDevice> AvailableDevices { get; } = new ObservableCollection<IStreamingDevice>();
        public ObservableCollection<IChannel> AvailableChannels { get; } = new ObservableCollection<IChannel>();
        //public IStreamingDevice SelectedDevice
        //{
        //    get => _selectedDevice;
        //    set
        //    {
        //        _selectedDevice = value;
        //        GetAvailableChannels(_selectedDevice);
        //        RaisePropertyChanged();
        //    }
        //}
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
                if (value < 1) return;
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
                        GetAvailableChannels(device);
                }
            }

        }
        #endregion

        public void GetAvailableChannels(IStreamingDevice device)
        {
            try
            {
                foreach (var channel in device.DataChannels)
                {
                    if (!AvailableChannels.Any(x => x.Name == channel.Name))
                        AvailableChannels.Add(channel);
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
            //TODO might use this later could not find a good way to raise can execute change
            return true;
        }
        AddProfileModel addProfileModel;
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

                // Cast parameter to object array
                var parameters = parameter as object[];
                if (parameters == null || parameters.Length < 2)
                {
                    return;
                }

                // Get selected channels and devices from parameters
                var selectedChannels = ((IEnumerable)parameters[0]).Cast<IChannel>().ToList();
                var selectedDevices = ((IEnumerable)parameters[1]).Cast<IStreamingDevice>().ToList();

                if (!selectedChannels.Any() || !selectedDevices.Any())
                {
                    return;
                }

                // Initialize the AddProfileModel
                var addProfileModel = new AddProfileModel
                {
                    ProfileList = new List<Profile>()
                };

                // Create a new profile and populate its devices
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

                        // Add the selected channels to the device
                        foreach (var dataChannel in selectedDevice.DataChannels)
                        {
                            // Check if this channel is selected
                            var isSelected = selectedChannels.Any(sc => sc.Name == dataChannel.Name);

                            var profileChannel = new ProfileChannel
                            {
                                Name = dataChannel.Name,
                                Type = dataChannel.TypeString.ToString(),
                                IsChannelActive = isSelected // Set IsChannelActive based on selection
                            };

                            // Add the channel to the device's channels list
                            device.Channels.Add(profileChannel);
                        }

                        // Add the device to the profile's devices collection
                        newProfile.Devices.Add(device);
                    }
                }

                // Add the profile to the ProfileList
                addProfileModel.ProfileList.Add(newProfile);

                // Log or perform further operations with the profile
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
