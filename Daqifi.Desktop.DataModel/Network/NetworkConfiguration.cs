﻿using Daqifi.Desktop.DataModel.Common;

namespace Daqifi.Desktop.DataModel.Network;

public class NetworkConfiguration: ObservableObject
{
    #region Private Data
    private WifiMode _mode;
    private WifiSecurityType _securityType;
    private string _ssid;
    private string _password;
    #endregion

    #region Properties
    public WifiMode Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            NotifyPropertyChanged(nameof(Mode));
        }
    }

    public WifiSecurityType SecurityType
    {
        get => _securityType;
        set
        {
            _securityType = value;
            NotifyPropertyChanged(nameof(SecurityType));
        }
    }

    public string Ssid
    {
        get => _ssid;
        set 
        { 
            _ssid = value;
            NotifyPropertyChanged(nameof(Ssid));
        }
    }

    public string Password
    {
        get => _password;
        set 
        { 
            _password = value;
            NotifyPropertyChanged(nameof(Password));
        }
    }
    #endregion
}