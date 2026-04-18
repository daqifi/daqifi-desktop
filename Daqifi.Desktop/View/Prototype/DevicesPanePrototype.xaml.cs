using System.Windows;
using Daqifi.Desktop.ViewModels;
using RadioButton = System.Windows.Controls.RadioButton;
using UserControl = System.Windows.Controls.UserControl;

namespace Daqifi.Desktop.View.Prototype;

public partial class DevicesPanePrototype : UserControl
{
    public DevicesPanePrototype()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Tab-switches on the host TabControl trigger Unloaded (which disposes
        // the VM) and then Loaded when the tab comes back. Recreate the VM so
        // a returning tab gets a fresh Rebuild and picks up devices connected
        // while the pane was detached.
        if (DataContext is not DevicesPaneViewModel)
        {
            var shell = Window.GetWindow(this)?.DataContext as DaqifiViewModel;
            DataContext = new DevicesPaneViewModel(shell);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
            DataContext = null;
        }
    }

    private void LoggingMode_Click(object sender, RoutedEventArgs e)
    {
        // The logging-mode setter on DaqifiViewModel takes the string label and
        // drives device SwitchMode + manager state; we surface it as a
        // segmented toggle and write the label back on click.
        if (sender is RadioButton { Tag: string mode } &&
            Window.GetWindow(this)?.DataContext is DaqifiViewModel shell)
        {
            shell.SelectedLoggingMode = mode;
        }
    }
}
