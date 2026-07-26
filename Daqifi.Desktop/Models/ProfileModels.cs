using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Daqifi.Desktop.Models;

public partial class Profile : ObservableObject
{
    [ObservableProperty]
    private string name = string.Empty;
    [ObservableProperty]
    private Guid profileId;
    [ObservableProperty]
    private DateTime createdOn;
    [ObservableProperty]
    private bool isProfileActive;
    [ObservableProperty]
    private ObservableCollection<ProfileDevice> devices = [];
}

public partial class ProfileDevice : ObservableObject
{
    [ObservableProperty]
    private string deviceName = string.Empty;
    [ObservableProperty]
    private string devicePartName = string.Empty;
    [ObservableProperty]
    private string deviceSerialNo = string.Empty;
    [ObservableProperty]
    private string macAddress = string.Empty;
    [ObservableProperty]
    private int samplingFrequency;
    [ObservableProperty]
    private List<ProfileChannel> channels = [];
}

public partial class ProfileChannel : ObservableObject
{
    [ObservableProperty]
    private string name = string.Empty;
    [ObservableProperty]
    private string serialNo = string.Empty;
    [ObservableProperty]
    private string type = string.Empty;
    [ObservableProperty]
    private bool isChannelActive;
}
