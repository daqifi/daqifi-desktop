using System.ComponentModel;

namespace Daqifi.Desktop.DataModel.Network
{
    public enum WifiSecurityType
    {
        [Description("None (Open Network)")]
        None = 0,

        [Description("WEP-40")]
        Wep40 = 1,

        [Description("WEP-104")]
        Wep104 = 2,

        [Description("WPA Pass Phrase")]
        WpaPskPhrase = 3,

        [Description("WPS Push Button")]
        WpsPushButton = 6,

        [Description("WPS Pin")]
        WpaPin = 7
    }
}
