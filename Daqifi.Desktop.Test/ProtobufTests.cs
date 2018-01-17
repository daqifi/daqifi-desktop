﻿using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;

namespace Daqifi.Desktop.Test
{
    [TestClass]
    public class ProtobufTests
    {
        [TestMethod]
        public void SystemInfoResponse_ValidInput_ParsesCorrectly()
        {
            
            // Result captured from wireshark from DAQifi Device
            //var systemInfoResponse = new byte[]
            //{
            //    0x0d, 0x0a, 0x0d, 0x0a, 0x3b, 0x08, 0xdb, 0xd1,
            //    0xf4, 0x38, 0x8a, 0x02, 0x04, 0xc0, 0xa8, 0x01,
            //    0x14, 0xb2, 0x02, 0x06, 0x00, 0x00, 0x00, 0x00,
            //    0x00, 0x00, 0xea, 0x02, 0x07, 0x4e, 0x59, 0x51,
            //    0x55, 0x49, 0x53, 0x54, 0xf0, 0x02, 0xa0, 0x4c,
            //    0x82, 0x03, 0x0f, 0x43, 0x65, 0x6e, 0x74, 0x75,
            //    0x72, 0x79, 0x4c, 0x69, 0x6e, 0x6b, 0x30, 0x36,
            //    0x32, 0x34, 0x88, 0x03, 0x04, 0xaa, 0x03, 0x00
            //};

            //var systemInfoResponse = new byte[]
            //{
            //    0x0d, 0x0a, 0x0d, 0x0a, 0x42, 0x08, 0xa8, 0x81,
            //    0xc6, 0xf4, 0x04, 0x12, 0x04, 0xe4, 0x01, 0xbc,
            //    0x01, 0x8a, 0x02, 0x04, 0xc0, 0xa8, 0x01, 0x14,
            //    0xb2, 0x02, 0x06, 0x00, 0x00, 0x00, 0x00, 0x00,
            //    0x00, 0xea, 0x02, 0x07, 0x4e, 0x59, 0x51, 0x55,
            //    0x49, 0x53, 0x54, 0xf0, 0x02, 0xa0, 0x4c, 0x82,
            //    0x03, 0x0f, 0x43, 0x65, 0x6e, 0x74, 0x75, 0x72,
            //    0x79, 0x4c, 0x69, 0x6e, 0x6b, 0x30, 0x36, 0x32,
            //    0x34, 0x88, 0x03, 0x04, 0xaa, 0x03, 0x00
            //};

            var systemInfoResponse = new byte[]
            {
                0x54, 0x08, 0xa6, 0xde, 0xc9, 0x81, 0x05, 0x48,
                0x01, 0x50, 0x00, 0x88, 0x01, 0x80, 0xe1, 0xeb,
                0x17, 0x90, 0x01, 0x10, 0x98, 0x01, 0x08, 0xa2,
                0x01, 0x00, 0xaa, 0x01, 0x00, 0xc0, 0x01, 0x80,
                0x20, 0xc8, 0x01, 0x80, 0x20, 0x8a, 0x02, 0x04,
                0xc0, 0xa8, 0x01, 0x14, 0xb2, 0x02, 0x06, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0xea, 0x02, 0x07,
                0x4e, 0x59, 0x51, 0x55, 0x49, 0x53, 0x54, 0xf0,
                0x02, 0xa0, 0x4c, 0x82, 0x03, 0x09, 0x44, 0x61,
                0x71, 0x69, 0x66, 0x69, 0x32, 0x31, 0x30, 0x88,
                0x03, 0x00, 0xaa, 0x03, 0x00
            };

            Stream stream = new MemoryStream(systemInfoResponse);

            //var protobufMessage = DaqifiOutMessage.ParseFrom(systemInfoResponse);

            var protobufMessage = DaqifiOutMessage.ParseDelimitedFrom(stream);

            Assert.IsTrue(protobufMessage.HasHostName);
            Assert.AreEqual("NYQUIST", protobufMessage.HostName);

            Assert.IsTrue(protobufMessage.HasWifiSecurityMode);
            Assert.AreEqual((uint)4, protobufMessage.WifiSecurityMode);

            Assert.IsTrue(protobufMessage.HasDevicePort);
            Assert.AreEqual((uint)9760, protobufMessage.DevicePort);

            Assert.IsTrue(protobufMessage.HasSsid);
            Assert.AreEqual("CenturyLink0624", protobufMessage.Ssid);

            Assert.IsTrue(protobufMessage.HasIpAddr);
            Assert.AreEqual("192.168.1.20", new IPAddress(protobufMessage.IpAddr.ToByteArray()).ToString());
        }
    }
}
