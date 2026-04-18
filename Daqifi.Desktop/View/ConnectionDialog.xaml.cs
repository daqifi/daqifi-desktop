using System.Windows;
using Daqifi.Desktop.ViewModels;

namespace Daqifi.Desktop.View;

/// <summary>
/// Interaction logic for ConnectionDialog.xaml
/// </summary>
public partial class ConnectionDialog
{
    private ConnectionDialogViewModel? _subscribedViewModel;

    public ConnectionDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.CloseRequested -= OnCloseRequested;
            _subscribedViewModel = null;
        }

        if (e.NewValue is ConnectionDialogViewModel viewModel)
        {
            _subscribedViewModel = viewModel;
            viewModel.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, System.EventArgs e)
    {
        // Marshal to the UI thread — connect commands complete on a worker thread.
        Dispatcher.BeginInvoke(new System.Action(() =>
        {
            if (IsLoaded) { Close(); }
        }));
    }

    private void MetroWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.CloseRequested -= OnCloseRequested;
            _subscribedViewModel = null;
        }

        if (DataContext is ConnectionDialogViewModel vm)
        {
            vm.Close();
        }
    }
}
