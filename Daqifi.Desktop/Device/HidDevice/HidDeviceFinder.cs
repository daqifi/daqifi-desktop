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
            var hidEnumerator = new HidFastReadEnumerator();
            var devices = hidEnumerator.Enumerate(VendorId, ProductId);

            foreach (var device in devices)
            {
                var daqifiDevice = new HidFirmwareDevice(device as HidFastReadDevice);
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
