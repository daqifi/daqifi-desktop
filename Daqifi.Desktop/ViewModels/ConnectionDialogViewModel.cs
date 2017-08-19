using Daqifi.Desktop.Commands;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Loggers;
using DAQifi.Desktop.Device;
using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.View;
using DAQifi.Desktop.View;
using DAQifi.Desktop.ViewModels;

namespace Daqifi.Desktop.ViewModels
{
    public class ConnectionDialogViewModel : ObservableObject
    {
        #region Private Variables
        private DaqifiDeviceFinder _wifiFinder;
        private SerialDeviceFinder _serialFinder;
        private HidDeviceFinder _hidDeviceFinder;
        private readonly IDialogService _dialogService;
        private bool _hasNoWiFiDevices = true;
        private bool _hasNoSerialDevices = true;
        private bool _hasNoHidDevices = true;
        #endregion

        #region Properties

        public AppLogger AppLogger = AppLogger.Instance;

        public ObservableCollection<DaqifiDevice> AvailableWiFiDevices { get; } = new ObservableCollection<DaqifiDevice>();
        public ObservableCollection<SerialDevice> AvailableSerialDevices { get; } = new ObservableCollection<SerialDevice>();
        public ObservableCollection<HidDevice> AvailableHidDevices { get; } = new ObservableCollection<HidDevice>();

        public bool HasNoWiFiDevices
        {
            get => _hasNoWiFiDevices;
            set
            {
                _hasNoWiFiDevices = value;
                NotifyPropertyChanged("HasNoWiFiDevices");
            }
        }

        public bool HasNoSerialDevices
        {
            get => _hasNoSerialDevices;
            set
            {
                _hasNoSerialDevices = value;
                NotifyPropertyChanged("HasNoSerialDevices");
            }
        }

        public bool HasNoHidDevices
        {
            get => _hasNoHidDevices;
            set
            {
                _hasNoHidDevices = value;
                NotifyPropertyChanged("HasNoHidDevices");
            }
        }
        #endregion

        #region Constructor
        public ConnectionDialogViewModel() : this(ServiceLocator.Resolve<IDialogService>()) { }

        public ConnectionDialogViewModel(IDialogService dialogService)
        {
            _dialogService = dialogService;
        }

        public void StartConnectionFinders()
        {
            _wifiFinder = new DaqifiDeviceFinder(30303);
            _wifiFinder.OnDeviceFound += WiFiDeviceFound;
            _wifiFinder.OnDeviceRemoved += WiFiDeviceRemoved;
            _wifiFinder.Start();

            _serialFinder = new SerialDeviceFinder();
            _serialFinder.OnDeviceFound += SerialDeviceFound;
            _serialFinder.OnDeviceRemoved += SerialDeviceRemoved;
            _serialFinder.Start();

            _hidDeviceFinder = new HidDeviceFinder();
            _hidDeviceFinder.OnDeviceFound += HidDeviceFound;
            _hidDeviceFinder.OnDeviceRemoved += HidDeviceRemoved;
            _hidDeviceFinder.Start();
        }
        #endregion

        #region Command Delegatges
        public ICommand ConnectCommand => new DelegateCommand(OnConnectSelectedItemsExecute, OnConnectSelectedItemsCanExecute);

        public ICommand ConnectSerialCommand => new DelegateCommand(ConnectSerial, CanConnectSerial);

        public ICommand ConnectHidCommand => new DelegateCommand(ConnectHid, CanConnectHid);

        private bool OnConnectSelectedItemsCanExecute(object selectedItems)
        {
            return true;
        }

        private bool CanConnectSerial(object selectedItems)
        {
            return true;
        }

        private bool CanConnectHid(object selectedItems)
        {
            return true;
        }

        private bool CanOpenFirmware(object selectedItems)
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
            foreach (var device in selectedDevices)
            {
                ConnectionManager.Instance.Connect(device);
            }
        }

        private void ConnectHid(object selectedItems)
        {
            _hidDeviceFinder.Stop();

            var selectedDevices = ((IEnumerable)selectedItems).Cast<HidDevice>();
            var hidDevice = selectedDevices.FirstOrDefault();
            if (hidDevice == null) return;

            //var view = ServiceLocator.GetInstance<SomeDialogView>();
            //view.ShowDialog();

            var firmwareDialogViewModel = new FirmwareDialogViewModel(hidDevice);
            _dialogService.ShowDialog<FirmwareDialog>(this, firmwareDialogViewModel);

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

        private void HidDeviceFound(object sender, IDevice device)
        {
            var hidDevice = device as HidDevice;

            if (hidDevice == null) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                AvailableHidDevices.Add(hidDevice);
                if (HasNoHidDevices) HasNoHidDevices = false;
            });
        }

        private void HidDeviceRemoved(object sender, IDevice device)
        {
            var hidDevice = device as HidDevice;

            if (hidDevice == null) return;
        }

        public void Close()
        {
            _wifiFinder?.Stop();
            _serialFinder?.Stop();
            _hidDeviceFinder?.Stop();
        }
    }
}