using HidLibrary;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;

namespace Daqifi.Desktop.Device.HidDevice
{
    public class HidDeviceFinder : IDeviceFinder
    {
        private readonly List<HidFirmwareDevice> _firmwareDevices = new List<HidFirmwareDevice>();
        private readonly HidFastReadEnumerator _hidEnumerator = new HidFastReadEnumerator();
        private readonly BackgroundWorker _hidDeviceFinderWorker;
        private const int VendorId = 0x4D8;
        private const int ProductId = 0x03C;
        public event OnDeviceFoundHandler OnDeviceFound;
        public event OnDeviceRemovedHandler OnDeviceRemoved;

        
        public HidDeviceFinder()
        {
            _hidDeviceFinderWorker = new BackgroundWorker() {WorkerSupportsCancellation = true};

            _hidDeviceFinderWorker.DoWork += delegate
            {
                while (true)
                {
                    var connectedHidDevices = _hidEnumerator.Enumerate(VendorId, ProductId).ToList();

                    // Add any devices that were connected
                    foreach (var device in connectedHidDevices)
                    {
                        var devicePath = device.DevicePath;
                        if (_firmwareDevices.FirstOrDefault(d => d.Name == devicePath) != null) continue;

                        var daqifiDevice = new HidFirmwareDevice(device as HidFastReadDevice) {Name = devicePath};
                        _firmwareDevices.Add(daqifiDevice);
                        NotifyDeviceFound(this, daqifiDevice);
                    }

                    var devicesToRemove = new List<HidFirmwareDevice>();
                    // Remove any devices that were disconnected
                    foreach (var device in _firmwareDevices)
                    {
                        var devicePath = device.Name;
                        if (connectedHidDevices.FirstOrDefault(d => d.DevicePath == devicePath) != null) continue;

                        NotifyDeviceRemoved(this, device);
                        devicesToRemove.Add(device);
                    }

                    foreach (var device in devicesToRemove)
                    {
                        _firmwareDevices.Remove(device);
                    }

                    // Check if finder has been stopped
                    if (_hidDeviceFinderWorker.CancellationPending)
                    {
                        break;
                    }

                    Thread.Sleep(1000);
                }
            };
        }

        public void Start()
        {
            _hidDeviceFinderWorker.RunWorkerAsync();
        }

        public void Stop()
        {
            _hidDeviceFinderWorker.CancelAsync();
        }

        public void NotifyDeviceFound(object sender, IDevice device)
        {
            OnDeviceFound?.Invoke(sender, device);
        }

        public void NotifyDeviceRemoved(object sender, IDevice device)
        {
            OnDeviceRemoved?.Invoke(sender, device);
        }
    }
}
