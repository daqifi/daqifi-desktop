using System.Windows;
using Daqifi.Desktop.ViewModels;

namespace Daqifi.Desktop.View;

/// <summary>
/// Interaction logic for ConnectionDialog.xaml
/// </summary>
public partial class ConnectionDialog
{
    public ConnectionDialog()
    {
        InitializeComponent();
    }

    private void btnConnect_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MetroWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        ConnectionDialogViewModel vm = DataContext as ConnectionDialogViewModel;
        vm.Close();
    }
}