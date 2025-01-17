
namespace Daqifi.Desktop.View
{
    /// <summary>
    /// Interaction logic for SuccessDialog.xaml
    /// </summary>
    public partial class SuccessDialog
    {
        public SuccessDialog()
        {
            InitializeComponent();
        }

        private void btnOk_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            Close();
        }
    }
}
