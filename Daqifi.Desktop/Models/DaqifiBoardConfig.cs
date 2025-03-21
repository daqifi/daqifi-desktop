namespace Daqifi.Desktop.Models;

public class DaqifiBoardConfig : ObservableObject
{
    #region Private Data
    private string _ipAddress;
    private string _portNumberString;
    private int _portNumber;
    private SdCardLoggingConfig _sdCardLogging;
    #endregion

    #region Properties
    public string IpAddress
    {
        get => _ipAddress;
        set 
        {
            if (value == _ipAddress) { return; }
            _ipAddress = value;
            NotifyPropertyChanged("IpAddress");
        }
    }

    public string PortNumberString
    {
        get => _portNumberString;
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
        get => _portNumber;
        set 
        {
            if (value == _portNumber) { return; }
            _portNumber = value;
            PortNumberString = _portNumber.ToString();
            NotifyPropertyChanged("PortNumber");
            NotifyPropertyChanged("PortNumberString");
        }
    }

    /// <summary>
    /// Gets or sets the SD card logging configuration
    /// </summary>
    public SdCardLoggingConfig SdCardLogging
    {
        get => _sdCardLogging;
        set
        {
            if (value == _sdCardLogging) { return; }
            _sdCardLogging = value;
            NotifyPropertyChanged(nameof(SdCardLogging));
        }
    }
    #endregion

    public DaqifiBoardConfig()
    {
        SdCardLogging = new SdCardLoggingConfig();
    }
}