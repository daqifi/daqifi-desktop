using Daqifi.Desktop.Common.Loggers;
using System.Windows;
using System.Windows.Controls;

namespace Daqifi.Desktop.View
{
    /// <summary>
    /// Interaction logic for AddprofileDialog.xaml
    /// </summary>
    public partial class AddprofileDialog
    {
        public AddprofileDialog()
        {
            InitializeComponent();
        }
        private readonly AppLogger AppLogger = AppLogger.Instance;

        private void btn_addprofile(object sender, RoutedEventArgs e)
        {
            Close();
        }
        private void SelectedDevice_Loaded(object sender, RoutedEventArgs e)
        {
            SelectedDevice.SelectedIndex = 0;
        }
        private void UpdateAddButtonState()
        {
            if (SelectedDevice.ItemsSource!=null&& ChannelList.ItemsSource != null)
            {
                bool isDeviceSelected = SelectedDevice.SelectedItems.Count > 0;
                bool isChannelSelected = ChannelList.SelectedItems.Count > 0;
                bool isProfileName = !string.IsNullOrWhiteSpace(ProfileName.Text);
                bool isFrequenctSelected = FrequencySlider.Value > 0;
                btnAdd.IsEnabled = isDeviceSelected && isChannelSelected && isFrequenctSelected && isProfileName;
            }
           
        }

        private void SelectedDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateAddButtonState();
        }

        private void ChannelList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateAddButtonState();
        }

        private void ProfileName_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateAddButtonState();
        }

        private void FrequencySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateAddButtonState();
        }
    }
}
