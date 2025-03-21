using System.Windows;

namespace Daqifi.Desktop.View;

/// <summary>
/// Interaction logic for ErrorDialog.xaml
/// </summary>
public partial class ErrorDialog
{
    public ErrorDialog()
    {
        InitializeComponent();
    }

    private void btnOk_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}