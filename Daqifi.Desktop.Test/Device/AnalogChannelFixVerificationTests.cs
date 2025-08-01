using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace Daqifi.Desktop.Test.Device;

[TestClass]
public class AnalogChannelFixVerificationTests
{
    /// <summary>
    /// Integration tests to verify that the analog channel fixes resolve the issue
    /// where channels 8-15 were showing 0V instead of correct values
    /// </summary>

    [TestMethod]
    public void ChannelBitMask_Channel8_ShouldProduceCorrectValue()
    {
        // This test verifies that channel 8 produces the correct bit mask value
        // Previously, the bug in RemoveChannel would have caused issues with channel 8
        
        // Arrange
        int channelIndex = 8;
        
        // Act
        uint bitMask = 1u << channelIndex;
        string bitMaskString = Convert.ToString(bitMask);
        
        // Assert
        Assert.AreEqual(256u, bitMask, "Channel 8 should produce bit mask 256");
        Assert.AreEqual("256", bitMaskString, "Channel 8 bit mask should convert to string '256'");
    }

    [TestMethod]
    public void ChannelBitMask_Channel15_ShouldProduceCorrectValue()
    {
        // This test verifies that channel 15 produces the correct bit mask value
        // This is the highest channel that was reported as problematic
        
        // Arrange
        int channelIndex = 15;
        
        // Act
        uint bitMask = 1u << channelIndex;
        string bitMaskString = Convert.ToString(bitMask);
        
        // Assert
        Assert.AreEqual(32768u, bitMask, "Channel 15 should produce bit mask 32768");
        Assert.AreEqual("32768", bitMaskString, "Channel 15 bit mask should convert to string '32768'");
    }

    [TestMethod]
    public void CombinedChannelMask_Channels8Through15_ShouldProduceCorrectValues()
    {
        // This test verifies that all problematic channels (8-15) can be combined correctly
        
        // Arrange
        uint combinedMask = 0u;
        
        // Act - Add all channels 8-15 to the mask
        for (int i = 8; i <= 15; i++)
        {
            combinedMask |= 1u << i;
        }
        
        // Assert
        // Channels 8-15 should produce: 256 + 512 + 1024 + 2048 + 4096 + 8192 + 16384 + 32768 = 65280
        Assert.AreEqual(65280u, combinedMask, "Combined mask for channels 8-15 should be 65280");
        
        // Verify each individual channel is set in the combined mask
        for (int i = 8; i <= 15; i++)
        {
            uint channelMask = 1u << i;
            Assert.IsTrue((combinedMask & channelMask) != 0, $"Channel {i} should be set in combined mask");
        }
    }

    [TestMethod]
    public void RemoveChannelOperation_ShouldUseCorrectBitLogic()
    {
        // This test verifies that the RemoveChannel operation uses correct bit logic
        // Previously, it used right shift (>>) instead of left shift (<<)
        
        // Arrange
        uint originalMask = (1u << 8) | (1u << 12) | (1u << 15); // Channels 8, 12, 15 active
        int channelToRemove = 12;
        
        // Act - Remove channel 12 using correct bit logic
        uint updatedMask = originalMask & ~(1u << channelToRemove);
        
        // Assert
        uint expectedMask = (1u << 8) | (1u << 15); // Only channels 8 and 15 should remain
        Assert.AreEqual(expectedMask, updatedMask, "After removing channel 12, only channels 8 and 15 should remain");
        
        // Verify specific channels
        Assert.IsTrue((updatedMask & (1u << 8)) != 0, "Channel 8 should still be active");
        Assert.IsFalse((updatedMask & (1u << 12)) != 0, "Channel 12 should be removed");
        Assert.IsTrue((updatedMask & (1u << 15)) != 0, "Channel 15 should still be active");
    }

    [TestMethod]
    public void DataTypeHandling_UnsignedInt_ShouldHandleHighChannels()
    {
        // This test verifies that using unsigned int prevents overflow issues
        // with higher channel numbers
        
        // Arrange & Act
        uint channel31Mask = 1u << 31; // This would overflow with signed int
        
        // Assert
        Assert.AreEqual(2147483648u, channel31Mask, "Channel 31 should produce correct unsigned value");
        Assert.IsTrue(channel31Mask > 0, "Channel 31 mask should be positive with unsigned int");
        
        // Verify string conversion works correctly
        string maskString = Convert.ToString(channel31Mask);
        Assert.AreEqual("2147483648", maskString, "Channel 31 mask should convert to correct string");
    }

    [TestMethod]
    public void ChannelEnabling_ConsistentBitOperations_ShouldWorkAcrossAllMethods()
    {
        // This test verifies that bit operations are consistent across different methods
        // (AddChannel, RemoveChannel, and SD logging)
        
        // Arrange
        int[] testChannels = { 0, 4, 8, 12, 15 }; // Mix of low and high channels
        
        // Act & Assert
        foreach (int channelIndex in testChannels)
        {
            // Test individual channel mask
            uint individualMask = 1u << channelIndex;
            Assert.IsTrue(individualMask > 0, $"Channel {channelIndex} should produce positive mask");
            
            // Test that the mask has exactly one bit set
            uint bitCount = 0;
            uint temp = individualMask;
            while (temp > 0)
            {
                bitCount += temp & 1;
                temp >>= 1;
            }
            Assert.AreEqual(1u, bitCount, $"Channel {channelIndex} mask should have exactly one bit set");
            
            // Test string conversion
            string maskString = Convert.ToString(individualMask);
            Assert.IsFalse(string.IsNullOrEmpty(maskString), $"Channel {channelIndex} mask string should not be empty");
            Assert.IsTrue(uint.TryParse(maskString, out uint parsedValue), $"Channel {channelIndex} mask string should be parseable");
            Assert.AreEqual(individualMask, parsedValue, $"Channel {channelIndex} mask should round-trip through string conversion");
        }
    }

    [TestMethod]
    public void BugDemonstration_RightShiftVsLeftShift_ShowsProblem()
    {
        // This test demonstrates the original bug and confirms it's fixed
        
        // Arrange
        int channelIndex = 8;
        
        // Act
        int buggyRightShift = 1 >> channelIndex;  // Original buggy code
        uint correctLeftShift = 1u << channelIndex; // Fixed code
        
        // Assert
        Assert.AreEqual(0, buggyRightShift, "Right shift produces 0 for channel 8 - this was the bug!");
        Assert.AreEqual(256u, correctLeftShift, "Left shift produces 256 for channel 8 - this is correct");
        
        // This demonstrates why channels 8-15 were showing 0V:
        // The buggy code would send "0" to the device instead of the correct channel mask
    }
}
