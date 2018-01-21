using HidLibrary;

namespace Daqifi.Desktop.Device.HidDevice
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
            //foreach (var streamingDevice in new HidSharp.HidDeviceLoader().GetDevices()
            //    .Where(d => d.VendorID == VendorId && d.ProductID == ProductId))
            //{
            //    var daqifiDevice = new HidFirmwareDevice(streamingDevice);
            //    NotifyDeviceFound(this, daqifiDevice);
            //}

            var devices = HidDevices.Enumerate(VendorId, ProductId);

            foreach (var device in devices)
            {
                var daqifiDevice = new HidFirmwareDevice(device);
                NotifyDeviceFound(this, daqifiDevice);
            }
        }

        public void Stop()
        {
            //throw new NotImplementedException();
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
