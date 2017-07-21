using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daqifi.Desktop.Bootloader.Test
{

    [TestClass]
    public class Pic32BootloaderMessageProducerTest
    {
        [TestMethod]
        public void CreateRequestVersionMessage_ReturnsCorrectValue()
        {
            // SOH DLE VC CRCL DLE CRCH EOT
            // StartOfHeader = 0x01 (1)
            // DataLinkEscape = 0x10 (16)
            // VersionCommand = 0x01 (1)
            // CrcLow = 0x21 (33)
            // DataLinkEscape = 0x10 (16)
            // CrcHigh = 0x10 (16)
            // EndOfTransmission = 0x04 (4)
            var correctValue = new byte[] {1, 16, 1, 33, 16, 16, 4};

            var messageProducer = new Pic32BootloaderMessageProducer();
            var actualValue = messageProducer.CreateRequestVersionMessage();

            CollectionAssert.AreEqual(correctValue,actualValue);
        }

        [TestMethod]
        public void CreateEraseFlashMessage_ReturnsCorrectValue()
        {
            // SOH RC CRCL CRCH EOT
            // StartOfHeader = 0x01 (1)
            // EraseFlash 
            var correctValue = new byte[] {};

            var messageProducer = new Pic32BootloaderMessageProducer();
            var actualValue = messageProducer.CreateEraseFlashMessage();

            Assert.Fail();
        }

        [TestMethod]
        public void CreateProgramFlashMessage_ReturnsCorrectValue()
        {
            // SOH EOT
            var correctValue = new byte[] { };

            var messageProducer = new Pic32BootloaderMessageProducer();

            Assert.Fail();
        }

        [TestMethod]
        public void CreateReadCrcMessage_ReturnsCorrectValue()
        {
            // SOH EOT
            var correctValue = new byte[] { };

            var messageProducer = new Pic32BootloaderMessageProducer();

            Assert.Fail();
        }

        [TestMethod]
        public void CreateJumpToApplicationMessage_ReturnsCorrectValue()
        {
            // SOH EOT
            var correctValue = new byte[] { };

            var messageProducer = new Pic32BootloaderMessageProducer();

            Assert.Fail();
        }
    }
}
