using System.Windows;

namespace Daqifi.Desktop.View;

/// <summary>
/// Interaction logic for AddChannelDialog.xaml
/// </summary>
public partial class AddChannelDialog
{
    public AddChannelDialog()
    {
        InitializeComponent();
    }

    private void btnAdd_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}