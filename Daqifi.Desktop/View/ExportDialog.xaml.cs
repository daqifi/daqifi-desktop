using System.Windows;

namespace Daqifi.Desktop.View;

/// <summary>
/// Interaction logic for ExportDialog.xaml
/// </summary>
public partial class ExportDialog
{
    public ExportDialog()
    {
        InitializeComponent();
    }

    private void btnCancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}