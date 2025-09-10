using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.Helpers;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.Models;
using System.Collections;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Daqifi.Desktop.ViewModels;

public partial class AddProfileDialogViewModel : ObservableObject
{
    #region Private Variables
    private string _profileName = "DaqifiProfile";
    private readonly IDialogService _dialogService;
    private int _selectedStreamingFrequency;
    #endregion

    #region Properties
    private readonly AppLogger AppLogger = AppLogger.Instance;


    private ObservableCollection<IStreamingDevice> _availableDevices = [];
    public ObservableCollection<IStreamingDevice> AvailableDevices
    {
        get => _availableDevices;
        set
        {
            _availableDevices = value;

            OnPropertyChanged();
        }
    }

    private ObservableCollection<IChannel> _availableChannels = [];
    public ObservableCollection<IChannel> AvailableChannels
    {
        get => _availableChannels;
        set
        {
            _availableChannels = value;

            OnPropertyChanged();
        }
    }
    public string ProfileName
    {
        get => _profileName;
        set
        {
            _profileName = value;

            OnPropertyChanged();
        }
    }
    private bool _canAddProfile;

    public bool canAddProfile
    {
        get => _canAddProfile;
        set
        {
            _canAddProfile = value;
            OnPropertyChanged();
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
            OnPropertyChanged();
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
                    // GetAvailableChannels(device);
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

            if (SelectedStreamingFrequency > 0)
            {
                canAddProfile = true;
            }
            else { canAddProfile = false; }

        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Error in adding profile ");
        }
    }
    public void GetAvailableChannels(IStreamingDevice device)
    {
        try
        {
            // Get channels that aren't already in the collection
            var newChannels = device.DataChannels
                .Where(channel => !AvailableChannels.Any(x => x.Name == channel.Name && x.DeviceSerialNo == channel.DeviceSerialNo))
                .ToList();

            // Add new channels
            foreach (var channel in newChannels)
            {
                AvailableChannels.Add(channel);
            }

            // Sort the entire collection naturally by name after adding new channels
            if (newChannels.Any())
            {
                var sortedChannels = AvailableChannels.NaturalOrderBy(channel => channel.Name).ToList();
                AvailableChannels.Clear();
                foreach (var channel in sortedChannels)
                {
                    AvailableChannels.Add(channel);
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Error in getting Available Channels");
        }

    }
    public void RemoveAvailableChannels(IStreamingDevice device)
    {
        try
        {
            // Find channels associated with the deselected device
            var channelsToRemove = AvailableChannels
                .Where(x => x.DeviceSerialNo == device.DeviceSerialNo)
                .ToList(); // Create a list to avoid modifying the collection while iterating

            // Remove the channels
            foreach (var channel in channelsToRemove)
            {
                var channelToRemove = AvailableChannels.ToList().FindIndex(x => x.DeviceSerialNo == channel.DeviceSerialNo && x.Name == channel.Name);
                AvailableChannels.RemoveAt(channelToRemove);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Error in removing Available Channels");
        }
    }

    #region Commands
    [RelayCommand]
    private void AddProfile(object parameter)
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
                        MacAddress = selectedDevice.MacAddress,
                        DeviceName = selectedDevice.Name,
                        DevicePartName = selectedDevice.DevicePartNumber,
                        DeviceSerialNo = selectedDevice.DeviceSerialNo,
                        SamplingFrequency = SelectedStreamingFrequency,
                        Channels = new List<ProfileChannel>()
                    };
                    foreach (var dataChannel in selectedDevice.DataChannels)
                    {
                        var isSelected = selectedChannels.Any(sc => sc.Name == dataChannel.Name && sc.DeviceSerialNo == selectedDevice.DeviceSerialNo);
                        if (isSelected)
                        {
                            var profileChannel = new ProfileChannel
                            {
                                Name = dataChannel.Name,
                                Type = dataChannel.TypeString,
                                IsChannelActive = isSelected
                            };
                            device.Channels.Add(profileChannel);
                        }

                    }
                    newProfile.Devices.Add(device);
                }
            }
            addProfileModel.ProfileList.Add(newProfile);
            LoggingManager.Instance.SubscribeProfile(newProfile);
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Error in OnSelectedProfileExecute");
        }
    }
    #endregion
}