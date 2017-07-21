using System;

namespace Daqifi.Desktop
{
    public class DaqifiBoardConfig : ObservableObject
    {
        #region Private Data
        private string _IPAddress;
        private string _portNumberString;
        private int _portNumber;
        #endregion

        #region Properties
        public string IPAddress
        {
            get { return _IPAddress; }
            set 
            { 
                if(value != _IPAddress)
                {
                    _IPAddress = value;
                    NotifyPropertyChanged("IPAddress");
                }
            }
        }

        public string PortNumberString
        {
            get { return _portNumberString; }
            set
            {
                _portNumberString = value;
                _portNumber = Convert.ToInt32(_portNumberString);
                NotifyPropertyChanged("PortNumberString");
                NotifyPropertyChanged("PortNumber");
            }
        }

        public int PortNumber
        {
            get { return _portNumber; }
            set 
            { 
                if(value != _portNumber)
                {
                    _portNumber = value;
                    PortNumberString = _portNumber.ToString();
                    NotifyPropertyChanged("PortNumber");
                    NotifyPropertyChanged("PortNumberString");
                }
            }
        }
        #endregion
    }
}