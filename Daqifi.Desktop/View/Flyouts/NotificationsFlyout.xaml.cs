using System.Diagnostics;

namespace Daqifi.Desktop.View.Flyouts
{
    /// <summary>
    /// Interaction logic for NotificationsFlyout.xaml
    /// </summary>
    public partial class NotificationsFlyout 
    {
        public NotificationsFlyout()
        {
            InitializeComponent();
        }

      
        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;

        }

    }
}
