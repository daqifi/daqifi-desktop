using Daqifi.Desktop.Device;
using HidLibrary;
using System;
using System.Linq;

namespace DAQifi.Desktop.Device
{
    public class HidDeviceFinder : IDeviceFinder
    {
        private const int VendorId = 0x4D8;
        private const int ProductId = 0x03C;
        public event OnDeviceFoundHandler OnDeviceFound;
        public event OnDeviceRemovedHandler OnDeviceRemoved;

        public void Start()
        {
            foreach (var device in HidDevices.Enumerate()
                .Where(d => d.Attributes.VendorId == VendorId && d.Attributes.ProductId == ProductId))
            {
                var daqifiDevice = new HidDevice(device);
                NotifyDeviceFound(this, daqifiDevice);
            }
        }

        public void Stop()
        {
            //throw new NotImplementedException();
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
