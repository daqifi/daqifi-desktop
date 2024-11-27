using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daqifi.Desktop.Bootloader.Test
{
    [TestClass]
    public class Crc16Test
    {
        [TestMethod]
        public void CalculateCrc_BasicInput_HasCorrectOutput()
        {
            //  var data = new byte[] { 0x01 };
            ////  var crc16 = new Crc16(data);
            //  var crc = crc16.Crc;
            //  var crcl = crc16.Low;
            //  var crch = crc16.High;

            //  Assert.AreEqual(0x1021, crc);
            //  Assert.AreEqual(0x10,crch);
            //  Assert.AreEqual(0x21, crcl);
        }

        [TestMethod]
        public void CalculateCrc_AdvancedInput_HasCorrectOutput()
        {
            //var data = new byte[] { 0x1, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0xC, 0x0, 0xF, 0xF, 0xB, 0xD, 0x2, 0x7, 0x3,
            //    0xC, 0x0, 0x0, 0xB, 0xF, 0xA, 0xF, 0x3, 0x8, 0x0, 0x0, 0xB, 0xE, 0xA, 0xF, 0x3, 0x4, 0x0, 0x0, 0xB, 0x7,
            //    0xA, 0xF, 0x6, 0x4 };
            ////var crc16 = new Crc16(data);
            //var crc = crc16.Crc;
            //var crcl = crc16.Low;
            //var crch = crc16.High;

            //Assert.AreEqual(0x507, crc);
            //Assert.AreEqual(0x05, crch);
            //Assert.AreEqual(0x07, crcl);
        }
    }
}
