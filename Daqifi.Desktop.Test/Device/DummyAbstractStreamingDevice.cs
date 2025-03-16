using Daqifi.Desktop.Device;
namespace Daqifi.Desktop.Test.Device
{
    public class DummyAbstractStreamingDevice : AbstractStreamingDevice
    {
        public override ConnectionType ConnectionType => ConnectionType.Usb; // Default to USB for testing

        public override bool Connect()
        {
            return true;
        }

        public override bool Disconnect()
        {
            throw new System.NotImplementedException();
        }

        public override bool Write(string command)
        {
            throw new System.NotImplementedException();
        }
    }
}
