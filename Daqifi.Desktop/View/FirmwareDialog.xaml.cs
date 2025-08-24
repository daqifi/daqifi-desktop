using MahApps.Metro.Controls;

namespace Daqifi.Desktop.View;

/// <summary>
/// Interaction logic for FirmwareDialog.xaml
/// </summary>
public partial class FirmwareDialog : MetroWindow
{
    public FirmwareDialog()
    {
        InitializeComponent();
    }

    private void btnCancel_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Close();
    }

    private void btnOk_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Close();
    }
}