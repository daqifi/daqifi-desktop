using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Logger;
using System.Collections;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Daqifi.Desktop.ViewModels;

public partial class AddChannelDialogViewModel : ObservableObject
{
    #region Private Variables
    private IStreamingDevice _selectedDevice;
    [ObservableProperty]
    private ObservableCollection<IStreamingDevice> _availableDevices = [];
    [ObservableProperty]
    private ObservableCollection<IChannel> _availableChannels = [];
    #endregion

    #region Properties
    public IStreamingDevice SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            _selectedDevice = value;
            GetAvailableChannels(_selectedDevice);
            OnPropertyChanged();
        }
    }
    #endregion

    #region Constructor
    public AddChannelDialogViewModel()
    {
        foreach (var device in ConnectionManager.Instance.ConnectedDevices)
        {
            AvailableDevices.Add(device);
        }
        if (AvailableDevices.Count > 0) SelectedDevice = AvailableDevices.ElementAt(0);
    }
    #endregion

    public void GetAvailableChannels(IStreamingDevice device)
    {
        AvailableChannels.Clear();

        foreach(var channel in device.DataChannels)
        {
            if(!channel.IsActive) AvailableChannels.Add(channel);
        }
    }

    #region Commands
    [RelayCommand]
    private void AddChannel(object selectedItems)
    {
        var selectedChannels = ((IEnumerable)selectedItems).Cast<IChannel>().ToList();

        if(selectedChannels.Count == 0)
        {
            return;
        }

        foreach (var channel in selectedChannels)
        {
            SelectedDevice.AddChannel(channel);
            LoggingManager.Instance.Subscribe(channel);
        }
    }
    #endregion
}