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
        private string _profileName;
        private readonly IDialogService _dialogService;
        private int _selectedStreamingFrequency;
        #endregion

        #region Properties
        public AppLogger AppLogger = AppLogger.Instance;

        public ObservableCollection<IStreamingDevice> AvailableDevices { get; } = new ObservableCollection<IStreamingDevice>();
        public ObservableCollection<IChannel> AvailableChannels { get; } = new ObservableCollection<IChannel>();

        public IStreamingDevice SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                _selectedDevice = value;
                GetAvailableChannels(_selectedDevice);
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
            if (AvailableDevices.Count > 0) SelectedDevice = AvailableDevices.ElementAt(0);
        }
        #endregion

        public void GetAvailableChannels(IStreamingDevice device)
        {
            try
            {
                AvailableChannels.Clear();
                foreach (var channel in device.DataChannels)
                {
                    /*if (!channel.IsActive)*/
                    AvailableChannels.Add(channel);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Error in getting Available Channels");
            }

        }


        #region Command Delegatges
        public ICommand AddProfileCommand => new DelegateCommand(OnSelectedProfileExecute, OnSelectedProfileCanExecute);

        private bool OnSelectedProfileCanExecute(object selectedItems)
        {
            //TODO might use this later could not find a good way to raise can execute change
            return true;
        }

        private void OnSelectedProfileExecute(object selectedItems)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ProfileName))
                {
                    // _dialogService.ShowDialog<ErrorDialog>(this, new ErrorDialogViewModel("Profile name is required."));
                    return;
                }
                if (AvailableDevices == null || AvailableDevices.Count == 0)
                {
                    // _dialogService.ShowDialog<ErrorDialog>(this, new ErrorDialogViewModel("Available devices are required."));
                    return;
                }
                if (SelectedDevice?.DataChannels?.Count == 0)
                {
                    // _dialogService.ShowDialog<ErrorDialog>(this, new ErrorDialogViewModel("Selected device must have data channels."));
                    return;
                }
                if (SelectedStreamingFrequency == 0)
                {
                    // _dialogService.ShowDialog<ErrorDialog>(this, new ErrorDialogViewModel("Streaming frequency must be greater than 0."));
                    return;
                }

                // Get selected channels
                var selectedChannels = ((IEnumerable)selectedItems).Cast<IChannel>().ToList();
                if (!selectedChannels.Any())
                    return;

                if (SelectedDevice != null && SelectedDevice.DataChannels.Count > 0)
                {
                    // Initialize the profile and device objects
                    var addProfileModel = new AddProfileModel
                    {
                        ProfileList = new List<Profile>
            {
                new Profile
                {
                    Name = ProfileName,
                    ProfileId = Guid.NewGuid(),
                    CreatedOn = DateTime.Now,
                    Devices = new ObservableCollection<ProfileDevice>
                    {
                        new ProfileDevice
                        {
                            MACAddress = SelectedDevice.MacAddress,
                            DeviceName = SelectedDevice.Name,
                            DevicePartName = SelectedDevice.DevicePartNumber,
                            DeviceSerialNo = SelectedDevice.DeviceSerialNo,
                            SamplingFrequency = SelectedStreamingFrequency,
                            Channels = new List<ProfileChannel>()
                        }
                    }
                }
            }
                    };

                    // Add all channels from SelectedDevice.DataChannels
                    foreach (var dataChannel in SelectedDevice.DataChannels)
                    {
                        // Check if this channel is in the selected channels list
                        var isSelected = selectedChannels.Any(sc => sc.Name == dataChannel.Name);

                        var profileChannel = new ProfileChannel
                        {
                            Name = dataChannel.Name,
                            Type = dataChannel.TypeString.ToString(),
                            IsChannelActive = isSelected ? true : false // Set IsChannelActive based on whether it's selected or not
                        };

                        // Add the channel to the profile
                        addProfileModel.ProfileList[0].Devices[0].Channels.Add(profileChannel);
                    }

                    // Subscribe the profile (logging or any additional operations)
                    LoggingManager.Instance.SubscribeProfile(addProfileModel.ProfileList[0]);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Error in getting on selected profile execute");
            }
        }

        #endregion
    }
}
