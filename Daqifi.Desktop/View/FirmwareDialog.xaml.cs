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

        // The view model owns a CancellationTokenSource for the in-flight update. Nothing else in the
        // dialog lifecycle disposes it, so release it when the window closes.
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
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