using Daqifi.Desktop.IO.Messages.Decoders;
using Google.Protobuf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Daqifi.Desktop.IO.Test.Messages.Decoders
{
    [TestClass]
    public class ProtobufDecoderTests
    {

        [TestMethod]
        public void GetIpAddressString_ValidProtobuf_ReturnsCorrectIpAddress()
        {
            var ipAddressBytes = new byte[] { 0xC0, 0xA8, 0x00, 0x2D };

            var mockMessage = new DaqifiOutMessage
            {
                IpAddr = ByteString.CopyFrom(ipAddressBytes),
            };


            var ipAddress = ProtobufDecoder.GetIpAddressString(mockMessage);

            Assert.AreEqual("192.168.0.45", ipAddress);
        }

        [TestMethod]
        public void GetMacAddress_ValidProtobuf_ReturnsCorrectIpAddress()
        {
            var macAddressBytes = new byte[] { 0x00, 0x1E, 0xC0, 0x33, 0xB8, 0xBE };

            //var mockMessage = new Mock<DaqifiOutMessage>();
            //mockMessage.Setup(p => p.IpAddr != null).Returns(true);
            //mockMessage.Setup(p => p.MacAddr).Returns(ByteString.CopyFrom(macAddressBytes));

            var mockMessage = new DaqifiOutMessage
            {
                IpAddr = ByteString.CopyFrom(macAddressBytes),
                MacAddr = ByteString.CopyFrom(macAddressBytes)
            };

            var macAddress = ProtobufDecoder.GetMacAddressString(mockMessage);

            Assert.AreEqual("00-1E-C0-33-B8-BE", macAddress);
        }
    }
}
