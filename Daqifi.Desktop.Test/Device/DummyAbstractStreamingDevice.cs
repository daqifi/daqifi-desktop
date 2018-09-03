
using Daqifi.Desktop.Device;

namespace Daqifi.Desktop.Test.Device
{
    public class DummyAbstractStreamingDevice : AbstractStreamingDevice
    {
        public override bool Connect()
        {
            throw new System.NotImplementedException();
        }

        public override bool Disconnect()
        {
            throw new System.NotImplementedException();
        }
    }
}
