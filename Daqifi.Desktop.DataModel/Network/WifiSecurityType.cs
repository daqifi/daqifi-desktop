using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

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

        [Description("WEP-PSK Phrase")]
        WpaPskPhrase = 3,

        [Description("WEP-PSK Key")]
        WpaPskKey = 4
    }
}
