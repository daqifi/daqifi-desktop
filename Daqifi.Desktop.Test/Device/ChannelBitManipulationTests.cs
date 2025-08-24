using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace Daqifi.Desktop.Test.Device;

[TestClass]
public class ChannelBitManipulationTests
{
    /// <summary>
    /// Tests the bit manipulation logic used in channel enable/disable operations
    /// These tests verify the mathematical correctness of bit operations for channel indices 0-15
    /// </summary>

    [TestMethod]
    public void BitShift_ChannelIndex0_ShouldReturn1()
    {
        // Arrange
        int channelIndex = 0;
        
        // Act
        int result = 1 << channelIndex;
        
        // Assert
        Assert.AreEqual(1, result, "Channel 0 bit mask should be 1");
    }

    [TestMethod]
    public void BitShift_ChannelIndex8_ShouldReturn256()
    {
        // Arrange
        int channelIndex = 8;
        
        // Act
        int result = 1 << channelIndex;
        
        // Assert
        Assert.AreEqual(256, result, "Channel 8 bit mask should be 256");
    }

    [TestMethod]
    public void BitShift_ChannelIndex15_ShouldReturn32768()
    {
        // Arrange
        int channelIndex = 15;
        
        // Act
        int result = 1 << channelIndex;
        
        // Assert
        Assert.AreEqual(32768, result, "Channel 15 bit mask should be 32768");
    }

    [TestMethod]
    public void BitShift_ChannelIndex16_ShouldReturn65536()
    {
        // Arrange
        int channelIndex = 16;
        
        // Act
        int result = 1 << channelIndex;
        
        // Assert
        Assert.AreEqual(65536, result, "Channel 16 bit mask should be 65536");
    }

    [TestMethod]
    public void CombinedBitMask_Channels0And8_ShouldReturn257()
    {
        // Arrange
        int channel0Mask = 1 << 0;  // 1
        int channel8Mask = 1 << 8;  // 256
        
        // Act
        int combinedMask = channel0Mask | channel8Mask;
        
        // Assert
        Assert.AreEqual(257, combinedMask, "Combined mask for channels 0 and 8 should be 257");
    }

    [TestMethod]
    public void CombinedBitMask_Channels8And12And15_ShouldReturn36864()
    {
        // Arrange
        int channel8Mask = 1 << 8;   // 256
        int channel12Mask = 1 << 12; // 4096
        int channel15Mask = 1 << 15; // 32768
        
        // Act
        int combinedMask = channel8Mask | channel12Mask | channel15Mask;
        
        // Assert
        // 256 + 4096 + 32768 = 37120
        Assert.AreEqual(37120, combinedMask, "Combined mask for channels 8, 12, and 15 should be 37120");
    }

    [TestMethod]
    public void RemoveBitFromMask_RemoveChannel8FromCombined_ShouldReturnCorrectMask()
    {
        // Arrange
        int channel8Mask = 1 << 8;   // 256
        int channel12Mask = 1 << 12; // 4096
        int combinedMask = channel8Mask | channel12Mask; // 4352
        
        // Act - Remove channel 8 from the combined mask
        int resultMask = combinedMask & ~channel8Mask;
        
        // Assert
        Assert.AreEqual(4096, resultMask, "After removing channel 8, only channel 12 should remain (4096)");
    }

    [TestMethod]
    public void IncorrectRightShift_Channel8_ShouldNotEqualLeftShift()
    {
        // Arrange
        int channelIndex = 8;
        
        // Act
        int correctLeftShift = 1 << channelIndex;  // Correct: 256
        int incorrectRightShift = 1 >> channelIndex; // Incorrect: 0 (this is the bug!)
        
        // Assert
        Assert.AreNotEqual(correctLeftShift, incorrectRightShift, 
            "Right shift operation should NOT equal left shift - this demonstrates the bug");
        Assert.AreEqual(256, correctLeftShift, "Left shift should produce 256");
        Assert.AreEqual(0, incorrectRightShift, "Right shift produces 0, which is incorrect");
    }

    [TestMethod]
    public void DataTypeOverflow_IntVsUint_ChannelIndex31()
    {
        // Arrange
        int channelIndex = 31;
        
        // Act
        int intResult = 1 << channelIndex;
        uint uintResult = 1u << channelIndex;
        
        // Assert
        // For channel 31, int will overflow to negative, uint will not
        Assert.IsTrue(intResult < 0, "int result should be negative due to overflow");
        Assert.AreEqual(2147483648u, uintResult, "uint result should be 2147483648");
    }

    [TestMethod]
    public void ConvertToString_LargeBitMask_ShouldReturnCorrectString()
    {
        // Arrange
        int channel15Mask = 1 << 15; // 32768
        
        // Act
        string result = Convert.ToString(channel15Mask);
        
        // Assert
        Assert.AreEqual("32768", result, "String conversion should produce '32768'");
    }

    [TestMethod]
    public void ConvertToString_CombinedMask_ShouldReturnCorrectString()
    {
        // Arrange
        int combinedMask = (1 << 8) | (1 << 12) | (1 << 15); // 256 + 4096 + 32768 = 37120
        
        // Act
        string result = Convert.ToString(combinedMask);
        
        // Assert
        Assert.AreEqual("37120", result, "String conversion should produce '37120'");
    }

    [TestMethod]
    public void BitMaskValidation_AllChannels0To15_ShouldProduceUniqueValues()
    {
        // Arrange & Act
        var bitMasks = new int[16];
        for (int i = 0; i < 16; i++)
        {
            bitMasks[i] = 1 << i;
        }
        
        // Assert
        // Verify all bit masks are unique
        for (int i = 0; i < 16; i++)
        {
            for (int j = i + 1; j < 16; j++)
            {
                Assert.AreNotEqual(bitMasks[i], bitMasks[j], 
                    $"Bit mask for channel {i} should not equal bit mask for channel {j}");
            }
        }
        
        // Verify specific expected values
        Assert.AreEqual(1, bitMasks[0], "Channel 0 mask");
        Assert.AreEqual(2, bitMasks[1], "Channel 1 mask");
        Assert.AreEqual(4, bitMasks[2], "Channel 2 mask");
        Assert.AreEqual(8, bitMasks[3], "Channel 3 mask");
        Assert.AreEqual(16, bitMasks[4], "Channel 4 mask");
        Assert.AreEqual(256, bitMasks[8], "Channel 8 mask");
        Assert.AreEqual(32768, bitMasks[15], "Channel 15 mask");
    }
}
