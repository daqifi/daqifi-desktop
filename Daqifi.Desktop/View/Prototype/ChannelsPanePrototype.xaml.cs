using Daqifi.Desktop.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace Daqifi.Desktop.View.Prototype;

public partial class ChannelsPanePrototype : UserControl
{
    public ChannelsPanePrototype()
    {
        InitializeComponent();
        DataContext = new ChannelsPaneViewModel();
    }
}
