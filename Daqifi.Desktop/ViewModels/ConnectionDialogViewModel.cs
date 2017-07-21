using Daqifi.Desktop.Commands;
using Daqifi.Desktop.Device;
using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Daqifi.Desktop.Loggers;

namespace Daqifi.Desktop.ViewModels
{
    public class ConnectionDialogViewModel : ObservableObject
    {
        #region Private Variables
        private readonly DaqifiDeviceFinder _wifiFinder;
        private readonly SerialDeviceFinder _serialFinder;
        private bool _hasNoWiFiDevices = true;
        private bool _hasNoSerialDevices = true;
        #endregion

        #region Properties

        public AppLogger AppLogger = AppLogger.Instance;

        public ObservableCollection<DaqifiDevice> AvailableWiFiDevices { get; } = new ObservableCollection<DaqifiDevice>();
        public ObservableCollection<SerialDevice> AvailableSerialDevices { get; } = new ObservableCollection<SerialDevice>();

        public bool HasNoWiFiDevices
        {
            get { return _hasNoWiFiDevices; }
            set
            {
                _hasNoWiFiDevices = value;
                NotifyPropertyChanged("HasNoWiFiDevices");
            }
        }

        public bool HasNoSerialDevices
        {
            get { return _hasNoSerialDevices; }
            set
            {
                _hasNoSerialDevices = value;
                NotifyPropertyChanged("HasNoSerialDevices");
            }
        }
        #endregion

        #region Constructor
        public ConnectionDialogViewModel()
        {
            _wifiFinder = new DaqifiDeviceFinder(30303);
            _wifiFinder.OnDeviceFound += WiFiDeviceFound;
            _wifiFinder.OnDeviceRemoved += WiFiDeviceRemoved;
            _wifiFinder.Start();

            _serialFinder = new SerialDeviceFinder();
            _serialFinder.OnDeviceFound += SerialDeviceFound;
            _serialFinder.OnDeviceRemoved += SerialDeviceRemoved;
            _serialFinder.Start();
        }
        #endregion

        #region Command Delegatges
        public ICommand ConnectCommand
        {
            get { return new DelegateCommand(OnConnectSelectedItemsExecute, OnConnectSelectedItemsCanExecute); }
        }

        public ICommand ConnectSerialCommand
        {
            get { return new DelegateCommand(ConnectSerial, CanConnectSerial); }
        }

        private bool OnConnectSelectedItemsCanExecute(object selectedItems)
        {
            return true;
        }

        private bool CanConnectSerial(object selectedItems)
        {
            return true;
        }

        private void OnConnectSelectedItemsExecute(object selectedItems)
        {
            _wifiFinder.Stop();

            var selectedDevices = ((IEnumerable)selectedItems).Cast<IDevice>();
            foreach (var device in selectedDevices)
            {
                ConnectionManager.Instance.Connect(device);
            }            
        }

        private void ConnectSerial(object selectedItems)
        {
            _serialFinder.Stop();

            var selectedDevices = ((IEnumerable)selectedItems).Cast<IDevice>();
            foreach (IDevice device in selectedDevices)
            {
                ConnectionManager.Instance.Connect(device);
            }
        }
        #endregion

        private void WiFiDeviceFound(object sender, IDevice device)
        {
            var wifiDevice = device as DaqifiDevice;

            if (wifiDevice == null) return;

            if(AvailableWiFiDevices.FirstOrDefault(d => d.MACAddress == wifiDevice.MACAddress) == null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    AvailableWiFiDevices.Add(wifiDevice);
                    if (HasNoWiFiDevices) HasNoWiFiDevices = false;
                });
            }               
        }

        private void WiFiDeviceRemoved(object sender, IDevice device)
        {
            var wifiDevice = device as DaqifiDevice;

            if (wifiDevice == null) return;

            var matchingDevice = AvailableWiFiDevices.FirstOrDefault(d => d.MACAddress == wifiDevice.MACAddress);
            if (matchingDevice != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    AvailableWiFiDevices.Remove(matchingDevice);
                });
            }
        }

        private void SerialDeviceFound(object sender, IDevice device)
        {
            var serialDevice = device as SerialDevice;

            if (serialDevice == null) return;

            if (AvailableSerialDevices.FirstOrDefault(d => d.Port == serialDevice.Port) == null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    AvailableSerialDevices.Add(serialDevice);
                    if (HasNoSerialDevices) HasNoSerialDevices = false;
                });
            }
        }

        private void SerialDeviceRemoved(object sender, IDevice device)
        {
            var serialDevice = device as SerialDevice;

            if (serialDevice == null) return;

            var matchingDevice = AvailableSerialDevices.FirstOrDefault(d => d.Port.PortName == serialDevice.Port.PortName);
            if (matchingDevice != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    AvailableSerialDevices.Remove(matchingDevice);
                });
            }
        }

        public void Close()
        {
            _wifiFinder.Stop();
            _serialFinder.Stop();
        }
    }
}