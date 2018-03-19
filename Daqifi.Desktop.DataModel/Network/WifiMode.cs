using System.ComponentModel;

namespace Daqifi.Desktop.DataModel.Network
{
    public enum WifiMode
    {
        [Description("Self Hosted")]
        SelfHosted,

        [Description("Existing Network")]
        ExistingNetwork
    }
}
