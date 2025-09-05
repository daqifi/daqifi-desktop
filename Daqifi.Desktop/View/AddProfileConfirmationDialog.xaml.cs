using System.Windows;

namespace Daqifi.Desktop.View;

/// <summary>
/// Interaction logic for AddProfileConfirmationDialog.xaml
/// </summary>
public partial class AddProfileConfirmationDialog
{
    public AddProfileConfirmationDialog()
    {
        InitializeComponent();
    }

    private void ExistingProfileBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void CreateNewProfileBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}