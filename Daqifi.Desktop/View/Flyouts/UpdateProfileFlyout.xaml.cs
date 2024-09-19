﻿using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.Models;
using Daqifi.Desktop.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Daqifi.Desktop.View.Flyouts
{
    /// <summary>
    /// Interaction logic for UpdateProfileFlyout.xaml
    /// </summary>
    public partial class UpdateProfileFlyout
    {
        private bool _isInitializing = true;
        private readonly IDialogService _dialogService;
        public UpdateProfileFlyout() : this(ServiceLocator.Resolve<IDialogService>()) { }
        public UpdateProfileFlyout(IDialogService dialogService)
        {
            _dialogService = dialogService;
            InitializeComponent();
            LoggingManager.Instance.PropertyChanged -= UpdateChannelUi;
            LoggingManager.Instance.PropertyChanged += UpdateChannelUi;
        }
        public AppLogger AppLogger = AppLogger.Instance;

        private void UpdateChannelUi(object sender, PropertyChangedEventArgs e)
        {
            try
            {

                UpdatedProfileChannelList.SelectedItems.Clear();
                SelectedDevice.SelectedItems.Clear();
                foreach (var channel in LoggingManager.Instance.SelectedProfileChannels)
                {
                    if (channel.IsChannelActive == true)  // Assuming IsChannelActive is a string
                    {
                        UpdatedProfileChannelList.SelectedItems.Add(channel);
                    }
                }
                foreach (var device in LoggingManager.Instance.SelectedProfileDevices)
                {

                    SelectedDevice.SelectedItems.Add(device);

                }
            }
            catch (Exception ex)
            {

                AppLogger.Error(ex, "Error in updating ui of profile flyout");
            }
        }
        private void ListViewItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (LoggingManager.Instance.SelectedProfile != null)
                {
                    var item = sender as ListViewItem;
                    if (item.DataContext is ProfileChannel channel && channel != null)
                    {
                        foreach (var device in LoggingManager.Instance.SelectedProfile.Devices)
                        {
                            foreach (var Channel in device.Channels)
                            {
                                if (Channel.Name == channel.Name)
                                {
                                    if (item != null && item.IsSelected)
                                        Channel.IsChannelActive = false;
                                    else
                                        Channel.IsChannelActive = true;
                                }
                            }
                        }
                        // var data = LoggingManager.Instance.SelectedProfile.Devices[0].Channels.Where(x => x.Name == channel.Name).FirstOrDefault();

                    }
                    if (item.DataContext is ProfileDevice selecteddevice && selecteddevice != null)
                    {
                        var data = LoggingManager.Instance.SelectedProfile.Devices.Where(x => x.DeviceSerialNo == selecteddevice.DeviceSerialNo).FirstOrDefault();

                        if (data != null)
                        {
                            if (LoggingManager.Instance.SelectedProfile.Devices.Count == 1)
                            {
                                SelectedDevice.SelectedIndex= 0;
                                return;
                            }
                            else
                                LoggingManager.Instance.SelectedProfile.Devices.Remove(data);
                        }
                        else
                        {
                            LoggingManager.Instance.SelectedProfile.Devices.Add(data);
                        }
                    }
                    if (item.DataContext is IStreamingDevice connecteddevice && connecteddevice != null)
                    {
                        var data = LoggingManager.Instance.SelectedProfile.Devices.Where(x => x.DeviceSerialNo == connecteddevice.DeviceSerialNo).FirstOrDefault();
                        if (data == null)
                        {
                            var Adddevicedata = new ProfileDevice()
                            {
                                DeviceName = connecteddevice.Name,
                                DevicePartName = connecteddevice.DevicePartNumber,
                                DeviceSerialNo = connecteddevice.DeviceSerialNo,
                                MACAddress = connecteddevice.MacAddress,
                                Channels = LoggingManager.Instance.SelectedProfile.Devices[0].Channels,
                                SamplingFrequency = LoggingManager.Instance.SelectedProfile.Devices[0].SamplingFrequency
                            };
                            LoggingManager.Instance.SelectedProfile.Devices.Add(Adddevicedata);
                            AvilableDevices.SelectedItem = -1;
                        }
                    }
                    LoggingManager.Instance.UpdateProfileInXml(LoggingManager.Instance.SelectedProfile);
                }
            }
            catch (Exception ex)
            {

                AppLogger.Error(ex, "Error editing profile channels");
            }

        }
        private void UpdatedProfileChannelListSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }
        }

        private void UpdatedProfileNameLblChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                if (LoggingManager.Instance.SelectedProfile != null)
                {

                    if (sender is TextBox profilename && !string.IsNullOrWhiteSpace(profilename.Text))
                        LoggingManager.Instance.SelectedProfile.Name = profilename.Text;

                    LoggingManager.Instance.UpdateProfileInXml(LoggingManager.Instance.SelectedProfile);
                }
            }
            catch (Exception ex)
            {

                AppLogger.Error(ex, "Error editing profile Name");
            }


        }

        private void UpdatedProfileSamplingFrequencyLblvalueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                if (LoggingManager.Instance.SelectedProfile != null)
                {


                    if (sender is Slider freq && freq.Value != 0)
                        foreach (var item in LoggingManager.Instance.SelectedProfile.Devices)
                        {
                            item.SamplingFrequency = Convert.ToInt32(freq.Value);
                        }


                    LoggingManager.Instance.UpdateProfileInXml(LoggingManager.Instance.SelectedProfile);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Error editing profile device frequency");
            }


        }
    }
}