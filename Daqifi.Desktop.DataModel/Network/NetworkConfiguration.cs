using CommunityToolkit.Mvvm.ComponentModel;

namespace Daqifi.Desktop.DataModel.Network;

public partial class NetworkConfiguration: ObservableObject
{
    #region Private Data
    [ObservableProperty]
    private WifiMode _mode;

    [ObservableProperty]
    private WifiSecurityType _securityType;

    [ObservableProperty]
    private string _ssid;

    [ObservableProperty]
    private string _password;
    #endregion
}