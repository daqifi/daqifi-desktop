using Daqifi.Desktop.Common.Loggers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Profile;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

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
            
            bool isDeviceSelected = SelectedDevice.SelectedItems.Count > 0;
            bool isChannelSelected = ChannelList.SelectedItems.Count > 0;
            bool isProfileName = !string.IsNullOrWhiteSpace(ProfileName.Text);
            bool isFrequenctSelected = FrequencySlider.Value > 1;
            btnAdd.IsEnabled = isDeviceSelected && isChannelSelected&& isFrequenctSelected&& isProfileName;
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
