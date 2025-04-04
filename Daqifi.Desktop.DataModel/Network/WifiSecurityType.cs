using System.ComponentModel;

namespace Daqifi.Desktop.DataModel.Network;

public enum WifiSecurityType
{
    [Description("None (Open Network)")]
    None = 0,

    [Description("WPA Pass Phrase")]
    WpaPskPhrase = 3
}