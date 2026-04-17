using Daqifi.Desktop.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace Daqifi.Desktop.View.Prototype;

public partial class ChannelsPanePrototype : UserControl
{
    public ChannelsPanePrototype()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        // Tab-switches on the host TabControl trigger Unloaded (which disposes
        // the VM) and then Loaded when the tab comes back. Recreate the VM so
        // a returning tab gets a fresh Rebuild and picks up devices connected
        // while the pane was detached.
        if (DataContext is not ChannelsPaneViewModel)
        {
            DataContext = new ChannelsPaneViewModel();
        }
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
            DataContext = null;
        }
    }
}
