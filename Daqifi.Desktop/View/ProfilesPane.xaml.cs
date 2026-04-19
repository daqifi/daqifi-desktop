using Daqifi.Desktop.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace Daqifi.Desktop.View;

/// <summary>
/// Code-behind for the unified Profiles pane. Owns only the minimal lifecycle
/// wiring: constructs a <see cref="ProfilesPaneViewModel"/> on Loaded if one
/// has not already been injected, and tears it down on Unloaded. All profile
/// state and commands live on the view model.
/// </summary>
public partial class ProfilesPane : UserControl
{
    /// <summary>
    /// Initializes the view and subscribes to the Loaded / Unloaded events so
    /// the view model is created and cleaned up with the control.
    /// </summary>
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
