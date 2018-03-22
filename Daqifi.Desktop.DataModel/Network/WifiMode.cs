using System.ComponentModel;

namespace Daqifi.Desktop.DataModel.Network
{
    public enum WifiMode
    {
        [Description("Self Hosted")]
        SelfHosted = 4,

        [Description("Existing Network")]
        ExistingNetwork = 1
    }
}
