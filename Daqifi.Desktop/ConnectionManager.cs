using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Device.SerialDevice;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Management;
using System.Windows;

namespace Daqifi.Desktop
{
    public class ConnectionManager : ObservableObject
    {
        #region Private Variables
        private DAQifiConnectionStatus _connectionStatus = DAQifiConnectionStatus.Disconnected;
        private List<IStreamingDevice> _connectedDevices;
        private bool _isDisconnected = true;
        private ManagementEventWatcher _deviceRemovedWatcher;
        #endregion

        #region Properties
        public DAQifiConnectionStatus ConnectionStatus
        {
            get => _connectionStatus;
            set
            {
                _connectionStatus = value;
                UpdateStatusString();
                NotifyPropertyChanged("ConnectionStatus");

                IsDisconnected = _connectionStatus != DAQifiConnectionStatus.Connected;
            }
        }

        public string ConnectionStatusString { get; set; } = "Disconnected";

        public List<IStreamingDevice> ConnectedDevices
        {
            get => _connectedDevices;
            set
            {
                _connectedDevices = value;
                NotifyPropertyChanged("ConnectedDevices");
            }
        }

        public bool IsDisconnected
        {
            get => _isDisconnected;
            set 
            { 
                if(value != _isDisconnected)
                {
                    _isDisconnected = value;
                    NotifyPropertyChanged("IsDisconnected");
                }
            }
        }
        #endregion

        #region Singleton Constructor / Initalization
        private static readonly ConnectionManager instance = new ConnectionManager();

        private ConnectionManager()
        {
            ConnectedDevices = new List<IStreamingDevice>();

            // EventType 3 is Device Removal
            var deviceRemovedQuery = new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 3");

            _deviceRemovedWatcher = new ManagementEventWatcher(deviceRemovedQuery);
            _deviceRemovedWatcher.EventArrived += (sender, eventArgs) => CheckIfSerialDeviceWasRemoved();
            _deviceRemovedWatcher.Start();
        }

        public static ConnectionManager Instance => instance;

        #endregion

        public void Connect(IStreamingDevice device)
        {
            try
            {
                ConnectionStatus = DAQifiConnectionStatus.Connecting;
                if (!device.Connect()) return;
                ConnectedDevices.Add(device);
                NotifyPropertyChanged("ConnectedDevices");
                ConnectionStatus = DAQifiConnectionStatus.Connected;
                //TODO do we do something if the attempt to connect fails?
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error(ex, "Failed to Connect in Connection");
                ConnectionStatus = DAQifiConnectionStatus.Error;
            }
        }

        public void Disconnect(IStreamingDevice device)
        {
            try
            {
                device.Disconnect();
                ConnectedDevices.Remove(device);
                NotifyPropertyChanged("ConnectedDevices");
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error(ex, "Failed in Disconnect");
            }
        }

        public void Reboot(IStreamingDevice device)
        {
            try
            {
                device.Reboot();
                ConnectedDevices.Remove(device);
                NotifyPropertyChanged("ConnectedDevices");
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error(ex, "Failed in Reboot");
            }
        }

        public void UpdateStatusString()
        {
            switch(ConnectionStatus)
            {
                case DAQifiConnectionStatus.Disconnected:
                    ConnectionStatusString = "Disconnected";
                    break;
                case DAQifiConnectionStatus.Connecting:
                    ConnectionStatusString = "Connecting";
                    break;
                case DAQifiConnectionStatus.Connected:
                    ConnectionStatusString = "Connected";
                    break;
                case DAQifiConnectionStatus.Error:
                    ConnectionStatusString = "Error";
                    break;
            }
        }

        private void CheckIfSerialDeviceWasRemoved()
        {
            var bw = new BackgroundWorker();
            bw.DoWork += delegate
            {
                var availableSerialPorts = SerialDeviceHelper.GetAvailableDaqifiPorts();
                var devicesToRemove = new List<SerialStreamingDevice>();

                // Get a list of devices to remove
                foreach (var device in ConnectedDevices)
                {
                    if (!(device is SerialStreamingDevice)) continue;
                    var serialDevice = device as SerialStreamingDevice;

                    // Check if the device has been removed
                    if (!availableSerialPorts.Contains(serialDevice.Port.PortName))
                    {
                        devicesToRemove.Add(serialDevice);
                    }
                }

                // Remove the devices
                foreach (var serialDevice in devicesToRemove)
                {
                    Application.Current.Dispatcher.Invoke(delegate
                    {
                        Disconnect(serialDevice);
                    });
                }
            };

            bw.RunWorkerAsync();
        }
    }

    public enum DAQifiConnectionStatus
    {
        Disconnected,
        Connecting,
        Connected,
        Error
    }
}
