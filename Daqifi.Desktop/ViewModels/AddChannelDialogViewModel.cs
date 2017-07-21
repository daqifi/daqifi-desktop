using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Logger;
using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows.Input;
using Daqifi.Desktop.Commands;
using Daqifi.Desktop.Loggers;

namespace Daqifi.Desktop.ViewModels
{
    public class AddChannelDialogViewModel : ObservableObject
    {
        #region Private Variables
        private IDevice _selectedDevice;
        #endregion

        #region Properties
        public AppLogger AppLogger = AppLogger.Instance;

        public ObservableCollection<IDevice> AvailableDevices { get; } = new ObservableCollection<IDevice>();
        public ObservableCollection<IChannel> AvailableChannels { get; } = new ObservableCollection<IChannel>();

        public IDevice SelectedDevice
        {
            get { return _selectedDevice; }
            set
            {
                _selectedDevice = value;
                GetAvailableChannels(_selectedDevice);
                NotifyPropertyChanged("SelectedDevice");
            }
        }
        #endregion

        #region Constructor
        public AddChannelDialogViewModel()
        {
            foreach (IDevice device in ConnectionManager.Instance.ConnectedDevices)
            {
                AvailableDevices.Add(device);
            }
            if (AvailableDevices.Count > 0) SelectedDevice = AvailableDevices.ElementAt(0);
        }
        #endregion

        public void GetAvailableChannels(IDevice device)
        {
            AvailableChannels.Clear();

            foreach(IChannel channel in device.DataChannels)
            {
                if(!channel.IsActive) AvailableChannels.Add(channel);
            }
        }

        #region Command Delegatges
        public ICommand AddChannelCommand
        {
            get { return new DelegateCommand(OnSelectedChannelExecute, OnSelectedChannelCanExecute); }
        }

        private bool OnSelectedChannelCanExecute(object selectedItems)
        {
            //TODO might use this later could not find a good way to raise can execute change
            return true;
        }

        private void OnSelectedChannelExecute(object selectedItems)
        {
            var selectedChannels = ((IEnumerable)selectedItems).Cast<IChannel>();

            if(!selectedChannels.Any())
            {
                return;
            }

            foreach (var channel in selectedChannels)
            {
                SelectedDevice.AddChannel(channel);
                LoggingManager.Instance.Subscribe(channel);
                Thread.Sleep(100);
            }
        }
        #endregion
    }
}