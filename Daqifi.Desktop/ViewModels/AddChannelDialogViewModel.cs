using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Commands;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Logger;
using GalaSoft.MvvmLight;
using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace Daqifi.Desktop.ViewModels
{
    public class AddChannelDialogViewModel : ViewModelBase
    {
        #region Private Variables
        private IStreamingDevice _selectedDevice;
        #endregion

        #region Properties
        private AppLogger AppLogger = AppLogger.Instance;

        public ObservableCollection<IStreamingDevice> AvailableDevices { get; } = new ObservableCollection<IStreamingDevice>();
        public ObservableCollection<IChannel> AvailableChannels { get; } = new ObservableCollection<IChannel>();

        public IStreamingDevice SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                _selectedDevice = value;
                GetAvailableChannels(_selectedDevice);
                RaisePropertyChanged();
            }
        }
        #endregion

        #region Constructor
        public AddChannelDialogViewModel()
        {
            foreach (var device in ConnectionManager.Instance.ConnectedDevices)
            {
                AvailableDevices.Add(device);
            }
            if (AvailableDevices.Count > 0) SelectedDevice = AvailableDevices.ElementAt(0);
        }
        #endregion

        public void GetAvailableChannels(IStreamingDevice device)
        {
            AvailableChannels.Clear();

            foreach(var channel in device.DataChannels)
            {
                if(!channel.IsActive) AvailableChannels.Add(channel);
            }
        }

        #region Command Delegatges
        public ICommand AddChannelCommand => new DelegateCommand(OnSelectedChannelExecute, OnSelectedChannelCanExecute);

        private bool OnSelectedChannelCanExecute(object selectedItems)
        {
            //TODO might use this later could not find a good way to raise can execute change
            return true;
        }

        private void OnSelectedChannelExecute(object selectedItems)
        {
            var selectedChannels = ((IEnumerable)selectedItems).Cast<IChannel>().ToList();

            if(!selectedChannels.Any())
            {
                return;
            }

            foreach (var channel in selectedChannels)
            {
                SelectedDevice.AddChannel(channel);
                LoggingManager.Instance.Subscribe(channel);
            }
        }
        #endregion
    }
}