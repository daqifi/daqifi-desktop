using Daqifi.Desktop.Device;
using System;
using System.Linq;
using HidLibrary;

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
            ////hidsharp
            //foreach (var device in new HidSharp.HidDeviceLoader().GetDevices()
            //    .Where(d => d.VendorID == VendorId && d.ProductID == ProductId))
            //{
            //    var daqifiDevice = new HidDevice(device);
            //    NotifyDeviceFound(this, daqifiDevice);
            //}

            var devices = HidDevices.Enumerate(VendorId, ProductId);

            foreach (var device in devices)
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
