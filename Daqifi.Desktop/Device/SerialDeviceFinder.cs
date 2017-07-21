﻿using System.IO.Ports;
using System.Linq;
using System.Management;

namespace Daqifi.Desktop.Device
{
    public class SerialDeviceFinder : IDeviceFinder
    {
        #region Private Data
        private string[] _serialPorts = new string[] { };
        private SerialPort _serialPort;
        private static ManagementEventWatcher _deviceAddedWatcher;
        private static ManagementEventWatcher _deviceRemovedWatcher;
        public event OnDeviceFoundHandler OnDeviceFound;
        public event OnDeviceRemovedHandler OnDeviceRemoved;
        #endregion

        #region Constructor
        public SerialDeviceFinder()
        {
            WireUpSerialPortChanges();
        }
        #endregion

        private void WireUpSerialPortChanges()
        {
            try
            {
                var deviceAddedQuery = new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 2");
                var deviceRemovedQuery = new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 3");

                _deviceAddedWatcher = new ManagementEventWatcher(deviceAddedQuery);
                _deviceRemovedWatcher = new ManagementEventWatcher(deviceRemovedQuery);

                _deviceAddedWatcher.EventArrived += (sender, eventArgs) => RaisePortsChangedIfNecessary();
                _deviceRemovedWatcher.EventArrived += (sender, eventArgs) => RaisePortsChangedIfNecessary();
            }
            catch (ManagementException ex)
            {

            }
        }

        private void RaisePortsChangedIfNecessary()
        {
            lock (_serialPorts)
            {
                var availableSerialPorts = GetAvailableSerialPorts();
                var addedPorts = availableSerialPorts.Except(_serialPorts);
                var removedPorts = _serialPorts.Except(availableSerialPorts);

                foreach (string portName in addedPorts)
                {
                   var device = new SerialDevice(portName);
                   NotifyDeviceFound(this,device);
                }

                foreach (string portName in removedPorts)
                {
                    var device = new SerialDevice(portName);
                    NotifyDeviceRemoved(this, device);
                }

                _serialPorts = availableSerialPorts;
            }
        }

        public static string[] GetAvailableSerialPorts()
        {
            return SerialPort.GetPortNames();
        }

        public void Start()
        {
            RaisePortsChangedIfNecessary();
            _deviceAddedWatcher.Start();
            _deviceRemovedWatcher.Start();
        }

        public void Stop()
        {
            _deviceAddedWatcher.Stop();
            _deviceRemovedWatcher.Stop();
        }

        public void NotifyDeviceFound(object sender, IDevice device)
        {
            if (OnDeviceFound == null) return;
            OnDeviceFound(sender, device);
        }

        public void NotifyDeviceRemoved(object sender, IDevice device)
        {
            if (OnDeviceRemoved == null) return;
            OnDeviceRemoved(sender, device);
        }
    }
}
