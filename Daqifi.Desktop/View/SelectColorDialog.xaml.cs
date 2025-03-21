using System.Windows;

namespace Daqifi.Desktop.View;

/// <summary>
/// Interaction logic for SelectColorDialog.xaml
/// </summary>
public partial class SelectColorDialog
{
    public SelectColorDialog()
    {
        InitializeComponent();
    }

    private void btnSelect_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}