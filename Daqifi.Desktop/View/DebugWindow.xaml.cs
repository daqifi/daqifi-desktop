using System.Windows;
using Daqifi.Desktop.ViewModels;
using MahApps.Metro.Controls;

namespace Daqifi.Desktop.View
{
    /// <summary>
    /// Interaction logic for DebugWindow.xaml
    /// </summary>
    public partial class DebugWindow : MetroWindow
    {
        public DebugWindow(DaqifiViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
