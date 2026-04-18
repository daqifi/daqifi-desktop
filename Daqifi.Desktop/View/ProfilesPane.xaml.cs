using Daqifi.Desktop.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace Daqifi.Desktop.View;

public partial class ProfilesPane : UserControl
{
    public ProfilesPane()
    {
        InitializeComponent();
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not ProfilesPaneViewModel)
            DataContext = new ProfilesPaneViewModel();
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ProfilesPaneViewModel vm)
            vm.Cleanup();
        DataContext = null;
    }
}
