using CommunityToolkit.Mvvm.ComponentModel;

namespace Daqifi.Desktop.Models;

public partial class DaqifiBoardConfig : ObservableObject
{
    #region Private Data
    [ObservableProperty]
    private string _ipAddress;
    
    [ObservableProperty]
    private string _portNumberString;

    [ObservableProperty]
    private int _portNumber;

    [ObservableProperty]
    private SdCardLoggingConfig _sdCardLogging;
    #endregion

    partial void OnPortNumberStringChanged(string value)
    {
        if (int.TryParse(value, out int newPortNumber))
        {
            if (PortNumber != newPortNumber) 
            {
                PortNumber = newPortNumber; 
            }
        }
    }

    partial void OnPortNumberChanged(int value)
    {
        if (PortNumberString != value.ToString())
        {
            PortNumberString = value.ToString();
        }
    }

    public DaqifiBoardConfig()
    {
        SdCardLogging = new SdCardLoggingConfig();
    }
}