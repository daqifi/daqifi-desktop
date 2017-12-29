using Daqifi.Desktop.DataModel.Common;

namespace Daqifi.Desktop.DataModel.Device
{
    public class NetworkConfiguration: ObservableObject
    {
        private string _SSID;
        private string _securityType;
        private string _password;

        public string SSID
        {
            get => _SSID;
            set 
            { 
                _SSID = value;
                NotifyPropertyChanged("SSID");
            }
        }

        public string SecurityType
        {
            get => _securityType;
            set 
            {
                _securityType = value;
                NotifyPropertyChanged("SecurityType");
            }
        }

        public string Password
        {
            get => _password;
            set 
            { 
                _password = value;
                NotifyPropertyChanged("Password");
            }
        }
    }
}
