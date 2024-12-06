using System.Collections.ObjectModel;

namespace Daqifi.Desktop.Models
{
        public class AddProfileModel:ObservableObject
    {
        public List<Profile> ProfileList { get; set; }
    }

    public class Profile : ObservableObject
    {
        private string name;
        private Guid profileId;
        private DateTime createdOn;
        private bool isProfileActive;
        private ObservableCollection<ProfileDevice> devices;

        public string Name
        {
            get => name;
            set
            {
                name = value;
                NotifyPropertyChanged(nameof(Name));
            }
        }
        public DateTime CreatedOn
        {
            get => createdOn;
            set
            {
                createdOn = value;
                NotifyPropertyChanged(nameof(CreatedOn));
            }
        }

        public Guid ProfileId
        {
            get => profileId;
            set
            {
                profileId = value;
                NotifyPropertyChanged(nameof(ProfileId));
            }
        }

        public bool IsProfileActive
        {
            get => isProfileActive;
            set
            {
                isProfileActive = value;
                NotifyPropertyChanged(nameof(IsProfileActive));
            }
        }

        public ObservableCollection<ProfileDevice> Devices
        {
            get => devices;
            set
            {
                devices = value;
                NotifyPropertyChanged(nameof(Devices));
            }
        }


    }

    public class ProfileDevice : ObservableObject
    {
        private string deviceName;
        private string devicePartName;
        private string deviceSerialNo;
        private string macAddress;
        private int samplingFrequency;
        private List<ProfileChannel> channels;

        public string DeviceName
        {
            get => deviceName;
            set
            {
                deviceName = value;
                NotifyPropertyChanged(nameof(DeviceName));
            }
        }

        public string DevicePartName
        {
            get => devicePartName;
            set
            {
                devicePartName = value;
                NotifyPropertyChanged(nameof(DevicePartName));
            }
        }

        public string DeviceSerialNo
        {
            get => deviceSerialNo;
            set
            {
                deviceSerialNo = value;
                NotifyPropertyChanged(nameof(DeviceSerialNo));
            }
        }

        public string MACAddress
        {
            get => macAddress;
            set
            {
                macAddress = value;
                NotifyPropertyChanged(nameof(MACAddress));
            }
        }

        public int SamplingFrequency
        {
            get => samplingFrequency;
            set
            {
                samplingFrequency = value;
                NotifyPropertyChanged(nameof(SamplingFrequency));
            }
        }

        public List<ProfileChannel> Channels
        {
            get => channels;
            set
            {
                channels = value;
                NotifyPropertyChanged(nameof(Channels));
            }
        }


    }

    public class ProfileChannel : ObservableObject
    {
        private string name;

        public string Name
        {
            get => name;
            set
            {
                name = value;
                NotifyPropertyChanged(nameof(Name));
            }
        }
        private string type;

        public string Type
        {
            get => type;
            set
            {
                type = value;
                NotifyPropertyChanged(nameof(Type));
            }
        }

        private bool isChannelActive;

        public bool IsChannelActive
        {
            get => isChannelActive;
            set
            {
                isChannelActive = value;
                NotifyPropertyChanged(nameof(IsChannelActive));
            }
        }


    }

}
