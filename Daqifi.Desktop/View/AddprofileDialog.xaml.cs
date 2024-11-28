using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.ViewModels;
using System;
using System.Collections;
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
            if (SelectedDevice.ItemsSource != null && ChannelList.ItemsSource != null)
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
            var datacontext = this.DataContext as AddProfileDialogViewModel;

            if (datacontext != null)
            {
                var selectedDevices = ((IEnumerable)SelectedDevice.SelectedItems).Cast<IStreamingDevice>().ToList();
                // Add channels for newly selected devices
                foreach (IStreamingDevice addedDevice in e.AddedItems)
                {
                    if (selectedDevices.Any(x => x.DeviceSerialNo == addedDevice.DeviceSerialNo))
                    {
                        datacontext.GetAvailableChannels(addedDevice);
                    }
                }

                // Remove channels for deselected devices
                foreach (IStreamingDevice removedDevice in e.RemovedItems)
                {
                    if (!selectedDevices.Any(x => x.DeviceSerialNo == removedDevice.DeviceSerialNo))
                    {
                        datacontext.RemoveAvailableChannels(removedDevice); // Assume this method exists to remove channels
                    }
                }
            }
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
