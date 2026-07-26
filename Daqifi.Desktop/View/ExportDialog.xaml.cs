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

        // The view model owns a CancellationTokenSource for the in-flight export. Nothing else in the
        // dialog lifecycle disposes it, so release it when the window closes.
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
    }

    private void btnCancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void btnDone_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}