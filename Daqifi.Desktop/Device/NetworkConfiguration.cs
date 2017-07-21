namespace Daqifi.Desktop.Device
{
    public class NetworkConfiguration: ObservableObject
    {
        private string _SSID;
        private string _securityType;
        private string _password;

        public string SSID
        {
            get { return _SSID; }
            set 
            { 
                _SSID = value;
                NotifyPropertyChanged("SSID");
            }
        }

        public string SecurityType
        {
            get { return _securityType; }
            set 
            {
                _securityType = value;
                NotifyPropertyChanged("SecurityType");
            }
        }

        public string Password
        {
            get { return _password; }
            set 
            { 
                _password = value;
                NotifyPropertyChanged("Password");
            }
        }
    }
}
