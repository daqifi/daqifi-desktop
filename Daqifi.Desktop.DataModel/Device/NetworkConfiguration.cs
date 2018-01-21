using Daqifi.Desktop.DataModel.Common;

namespace Daqifi.Desktop.DataModel.Device
{
    public class NetworkConfiguration: ObservableObject
    {
        private string _ssid;
        private string _securityType;
        private string _password;

        public string Ssid
        {
            get => _ssid;
            set 
            { 
                _ssid = value;
                NotifyPropertyChanged("Ssid");
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
