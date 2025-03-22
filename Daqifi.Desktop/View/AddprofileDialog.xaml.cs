﻿using Daqifi.Desktop.Device;
using Daqifi.Desktop.ViewModels;
using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace Daqifi.Desktop.View;

/// <summary>
/// Interaction logic for AddprofileDialog.xaml
/// </summary>
public partial class AddprofileDialog
{
    public AddprofileDialog()
    {
        InitializeComponent();
    }

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
            var isDeviceSelected = SelectedDevice.SelectedItems.Count > 0;
            var isChannelSelected = ChannelList.SelectedItems.Count > 0;
            var isProfileName = !string.IsNullOrWhiteSpace(ProfileName.Text);
            var isFrequencySelected = FrequencySlider.Value > 0;
            btnAdd.IsEnabled = isDeviceSelected && isChannelSelected && isFrequencySelected && isProfileName;
        }

    }

    private void SelectedDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var datacontext = DataContext as AddProfileDialogViewModel;

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
                    datacontext.RemoveAvailableChannels(removedDevice); 
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