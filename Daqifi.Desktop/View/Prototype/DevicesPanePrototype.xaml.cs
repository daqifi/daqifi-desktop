using System.Windows;
using Daqifi.Desktop.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace Daqifi.Desktop.View.Prototype;

/// <summary>
/// Host UserControl for the unified Devices pane. Owns the
/// <see cref="DevicesPaneViewModel"/> lifecycle — recreates the VM on
/// Loaded (since TabControl switches trigger Unloaded → Loaded) so a
/// returning tab picks up devices connected while it was detached, and
/// disposes the VM on Unloaded to detach the singleton subscription.
/// </summary>
public partial class DevicesPanePrototype : UserControl
{
    /// <summary>Creates the pane and wires the Loaded/Unloaded VM lifecycle.</summary>
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
}
