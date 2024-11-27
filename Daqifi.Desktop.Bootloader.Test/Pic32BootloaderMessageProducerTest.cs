using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daqifi.Desktop.Bootloader.Test
{

    [TestClass]
    public class Pic32BootloaderMessageProducerTest
    {
        //    [TestMethod]
        //    public void CreateRequestVersionMessage_ReturnsCorrectValue()
        //    {
        //        // SOH DLE VC CRCL DLE CRCH EOT
        //        // StartOfHeader = 0x01 (1)
        //        // DataLinkEscape = 0x10 (16)
        //        // VersionCommand = 0x01 (1)
        //        // CrcLow = 0x21 (33)
        //        // DataLinkEscape = 0x10 (16)
        //        // CrcHigh = 0x10 (16)
        //        // EndOfTransmission = 0x04 (4)
        //        var correctValue = new byte[] {1, 16, 1, 33, 16, 16, 4};

        //        var messageProducer = new Pic32BootloaderMessageProducer();
        //        var actualValue = messageProducer.CreateRequestVersionMessage();

        //        CollectionAssert.AreEqual(correctValue,actualValue);
        //    }

        //    [TestMethod]
        //    public void CreateEraseFlashMessage_ReturnsCorrectValue()
        //    {
        //        // SOH RC CRCL CRCH EOT
        //        // StartOfHeader = 0x01 (1)
        //        // EraseFlashCommand = 0x02 (2)
        //        // CrcLow = 0x42 (66)
        //        // CrcHigh = 0x20 (32)
        //        // EndOfTransmission = 0x04 (4)
        //        var correctValue = new byte[] {1, 2, 66, 32, 4};

        //        var messageProducer = new Pic32BootloaderMessageProducer();
        //        var actualValue = messageProducer.CreateEraseFlashMessage();

        //        CollectionAssert.AreEqual(correctValue, actualValue);
        //    }

        //    [TestMethod]
        //    public void CreateProgramFlashMessage_ReturnsCorrectValue()
        //    {
        //        // TODO
        //    }

        //    public void CreateReadCrcMessage_ReturnsCorrectValue()
        //    {
        //        // TODO
        //    }

        //    [TestMethod]
        //    public void CreateJumpToApplicationMessage_ReturnsCorrectValue()
        //    {
        //        // SOH RC CRCL CRCH EOT
        //        // StartOfHeader = 0x01 (1)
        //        // JumptToAppCommand = 0x05 (5)
        //        // CrcLow = 0xA5 (66)
        //        // CrcHigh = 0x50 (32)
        //        // EndOfTransmission = 0x04 (4)
        //        var correctValue = new byte[] { 1, 5, 165, 80, 4 };

        //        var messageProducer = new Pic32BootloaderMessageProducer();
        //        var actualValue = messageProducer.CreateJumpToApplicationMessage();

        //        CollectionAssert.AreEqual(correctValue, actualValue);
        //    }
    }
}
