using Daqifi.Desktop.Device;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Remoting.Channels;
using System.Text;
using System.Threading.Tasks;

namespace Daqifi.Desktop.Models
{
    public class AddProfileModel
    {
        public List<Profile> ProfileList { get; set; }
    }

    public class Profile : INotifyPropertyChanged
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
                RaisePropertyChanged(nameof(Name));
            }
        }
        public DateTime CreatedOn
        {
            get => createdOn;
            set
            {
                createdOn = value;
                RaisePropertyChanged(nameof(CreatedOn));
            }
        }

        public Guid ProfileId
        {
            get => profileId;
            set
            {
                profileId = value;
                RaisePropertyChanged(nameof(ProfileId));
            }
        }

        public bool IsProfileActive
        {
            get => isProfileActive;
            set
            {
                isProfileActive = value;
                RaisePropertyChanged(nameof(IsProfileActive));
            }
        }

        public ObservableCollection<ProfileDevice> Devices
        {
            get => devices;
            set
            {
                devices = value;
                RaisePropertyChanged(nameof(Devices));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void RaisePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ProfileDevice : INotifyPropertyChanged
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
                RaisePropertyChanged(nameof(DeviceName));
            }
        }

        public string DevicePartName
        {
            get => devicePartName;
            set
            {
                devicePartName = value;
                RaisePropertyChanged(nameof(DevicePartName));
            }
        }

        public string DeviceSerialNo
        {
            get => deviceSerialNo;
            set
            {
                deviceSerialNo = value;
                RaisePropertyChanged(nameof(DeviceSerialNo));
            }
        }

        public string MACAddress
        {
            get => macAddress;
            set
            {
                macAddress = value;
                RaisePropertyChanged(nameof(MACAddress));
            }
        }

        public int SamplingFrequency
        {
            get => samplingFrequency;
            set
            {
                samplingFrequency = value;
                RaisePropertyChanged(nameof(SamplingFrequency));
            }
        }

        public List<ProfileChannel> Channels
        {
            get => channels;
            set
            {
                channels = value;
                RaisePropertyChanged(nameof(Channels));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void RaisePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ProfileChannel : INotifyPropertyChanged
    {
        private string name;

        public string Name
        {
            get => name;
            set
            {
                name = value;
                RaisePropertyChanged(nameof(Name));
            }
        }
        private string type;

        public string Type
        {
            get => type;
            set
            {
                type = value;
                RaisePropertyChanged(nameof(Type));
            }
        }

        private bool isChannelActive;

        public bool IsChannelActive
        {
            get => isChannelActive;
            set
            {
                isChannelActive = value;
                RaisePropertyChanged(nameof(IsChannelActive));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void RaisePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

}
