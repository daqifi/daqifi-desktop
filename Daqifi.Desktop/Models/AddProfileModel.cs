using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Daqifi.Desktop.Models;

public partial class AddProfileModel : ObservableObject
{
    public List<Profile> ProfileList { get; set; }
}

public partial class Profile : ObservableObject
{
    [ObservableProperty]
    private string name;
    [ObservableProperty]
    private Guid profileId;
    [ObservableProperty]
    private DateTime createdOn;
    [ObservableProperty]
    private bool isProfileActive;
    [ObservableProperty]
    private ObservableCollection<ProfileDevice> devices;
}

public partial class ProfileDevice : ObservableObject
{
    [ObservableProperty]
    private string deviceName;
    [ObservableProperty]
    private string devicePartName;
    [ObservableProperty]
    private string deviceSerialNo;
    [ObservableProperty]
    private string macAddress;
    [ObservableProperty]
    private int samplingFrequency;
    [ObservableProperty]
    private List<ProfileChannel> channels;
}

public partial class ProfileChannel : ObservableObject
{
    [ObservableProperty]
    private string name;
    [ObservableProperty]
    private string serialNo;
    [ObservableProperty]
    private string type;
    [ObservableProperty]
    private bool isChannelActive;
}