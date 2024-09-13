using Daqifi.Desktop.Common.Loggers;
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
                foreach (var channel in LoggingManager.Instance.SelectedProfileChannels)
                {
                    if (channel.IsChannelActive == true)  // Assuming IsChannelActive is a string
                    {
                        UpdatedProfileChannelList.SelectedItems.Add(channel);
                    }
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
                        var data = LoggingManager.Instance.SelectedProfile.Devices[0].Channels.Where(x => x.Name == channel.Name).FirstOrDefault();
                        if (item != null && item.IsSelected)
                            data.IsChannelActive = false;
                        else
                            data.IsChannelActive = true;
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
                        LoggingManager.Instance.SelectedProfile.Devices[0].SamplingFrequency = Convert.ToInt32(freq.Value);

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