using System.Windows;
using Daqifi.Desktop.ViewModels;

namespace Daqifi.Desktop.View;

/// <summary>
/// Interaction logic for DuplicateDeviceDialog.xaml
/// </summary>
public partial class DuplicateDeviceDialog
{
    public enum DuplicateDeviceDialogResult
    {
        KeepExisting,
        SwitchToNew,
        Cancel
    }

    public DuplicateDeviceDialogResult Result { get; private set; } = DuplicateDeviceDialogResult.Cancel;

    public DuplicateDeviceDialog()
    {
        InitializeComponent();
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        if (SwitchToNewRadio.IsChecked == true)
        {
            Result = DuplicateDeviceDialogResult.SwitchToNew;
        }
        else
        {
            Result = DuplicateDeviceDialogResult.KeepExisting;
        }

        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Result = DuplicateDeviceDialogResult.Cancel;
        DialogResult = false;
        Close();
    }
}