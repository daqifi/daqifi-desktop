using Daqifi.Desktop.Device;
using Daqifi.Core.Device.Network;
using Daqifi.Desktop.IO.Messages.Consumers;
using Daqifi.Desktop.IO.Messages.Producers;
using Daqifi.Core.Communication.Messages;
using Daqifi.Core.Device; // Added for DeviceType, DeviceTypeDetector from Core
using Moq;

namespace Daqifi.Desktop.Test.Device;

[TestClass]
public class AbstractStreamingDeviceTests
{
    /// <summary>
    /// These tests focus on the bit manipulation logic used in channel operations
    /// to identify the root cause of channels 8-15 showing 0V
    /// </summary>

    [TestMethod]
    public void RemoveChannelBitOperation_ShouldUseLeftShiftNotRightShift()
    {
        // This test demonstrates the critical bug in RemoveChannel method
        // The current implementation uses right shift (>>) instead of left shift (<<)

        // Arrange
        var channelIndex = 8;

        // Act - Current buggy implementation
        var buggyResult = 1 >> channelIndex;  // This is what the code currently does (WRONG)
        var correctResult = 1 << channelIndex; // This is what it should do

        // Assert
        Assert.AreEqual(0, buggyResult, "Right shift produces 0 for channel 8 - this is the bug!");
        Assert.AreEqual(256, correctResult, "Left shift should produce 256 for channel 8");
        Assert.AreNotEqual(buggyResult, correctResult, "The bug causes wrong bit mask calculation");
    }

    [TestMethod]
    public void ChannelSetByte_IntegerOverflow_ShouldUseUnsignedInt()
    {
        // This test demonstrates potential integer overflow issues with higher channel numbers

        // Arrange
        var channelIndex = 31;

        // Act
        var intResult = 1 << channelIndex;      // May overflow to negative
        var uintResult = 1u << channelIndex;   // Correct approach

        // Assert
        Assert.IsLessThan(0, intResult, "int overflows to negative for channel 31");
        Assert.AreEqual(2147483648u, uintResult, "uint handles channel 31 correctly");
    }

    [TestMethod]
    public void StringConversion_ChannelBitMask_ShouldProduceCorrectValues()
    {
        // This test verifies that bit mask to string conversion works correctly
        // for channels that are currently failing (8-15)

        // Arrange & Act
        var channel8Mask = Convert.ToString(1 << 8);   // Should be "256"
        var channel15Mask = Convert.ToString(1 << 15); // Should be "32768"

        // Assert
        Assert.AreEqual("256", channel8Mask, "Channel 8 bit mask string should be '256'");
        Assert.AreEqual("32768", channel15Mask, "Channel 15 bit mask string should be '32768'");
    }

    [TestMethod]
    public void DeviceType_Enum_ShouldHaveCorrectValues()
    {
        // Arrange & Act & Assert
        Assert.AreEqual(0, (int)DeviceType.Unknown, "Unknown should have value 0");
        Assert.AreEqual(1, (int)DeviceType.Nyquist1, "Nyquist1 should have value 1");
        Assert.AreEqual(2, (int)DeviceType.Nyquist2, "Nyquist2 should have value 2");
        Assert.AreEqual(3, (int)DeviceType.Nyquist3, "Nyquist3 should have value 3");
    }

    [TestMethod]
    public void DeviceType_EnumNames_ShouldMatchExpectedValues()
    {
        // Arrange & Act & Assert
        Assert.AreEqual("Unknown", DeviceType.Unknown.ToString());
        Assert.AreEqual("Nyquist1", DeviceType.Nyquist1.ToString());
        Assert.AreEqual("Nyquist2", DeviceType.Nyquist2.ToString());
        Assert.AreEqual("Nyquist3", DeviceType.Nyquist3.ToString());
    }

    [TestMethod]
    public void DeviceType_Property_ShouldDefaultToUnknown()
    {
        // Arrange & Act
        var device = new TestStreamingDevice();

        // Assert
        Assert.AreEqual(DeviceType.Unknown, device.DeviceType, "DeviceType should default to Unknown");
    }

    [TestMethod]
    public void DeviceType_Property_ShouldBeSettable()
    {
        // Arrange
        var device = new TestStreamingDevice();

        // Act
        device.DeviceType = DeviceType.Nyquist1;

        // Assert
        Assert.AreEqual(DeviceType.Nyquist1, device.DeviceType, "DeviceType should be settable to Nyquist1");

        // Act
        device.DeviceType = DeviceType.Nyquist3;

        // Assert
        Assert.AreEqual(DeviceType.Nyquist3, device.DeviceType, "DeviceType should be settable to Nyquist3");
    }

    [TestMethod]
    public void DeviceType_Property_ShouldNotifyPropertyChanged()
    {
        // Arrange
        var device = new TestStreamingDevice();
        var propertyChanged = false;
        device.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(device.DeviceType))
                propertyChanged = true;
        };

        // Act
        device.DeviceType = DeviceType.Nyquist1;

        // Assert
        Assert.IsTrue(propertyChanged, "DeviceType property should notify PropertyChanged");
    }

    [TestMethod]
    public void DetectFromPartNumber_ShouldReturnNyquist1_ForNq1()
    {
        // Arrange & Act
        var deviceType = DeviceTypeDetector.DetectFromPartNumber("Nq1");

        // Assert
        Assert.AreEqual(DeviceType.Nyquist1, deviceType, "Should detect Nyquist1 from Nq1 part number");
    }

    [TestMethod]
    public void DetectFromPartNumber_ShouldReturnNyquist3_ForNq3()
    {
        // Arrange & Act
        var deviceType = DeviceTypeDetector.DetectFromPartNumber("Nq3");

        // Assert
        Assert.AreEqual(DeviceType.Nyquist3, deviceType, "Should detect Nyquist3 from Nq3 part number");
    }

    [TestMethod]
    public void DetectFromPartNumber_ShouldBeCaseInsensitive()
    {
        // Arrange & Act
        var nq1Upper = DeviceTypeDetector.DetectFromPartNumber("NQ1");
        var nq1Lower = DeviceTypeDetector.DetectFromPartNumber("nq1");
        var nq1Mixed = DeviceTypeDetector.DetectFromPartNumber("Nq1");

        // Assert
        Assert.AreEqual(DeviceType.Nyquist1, nq1Upper, "Should detect Nyquist1 from uppercase NQ1");
        Assert.AreEqual(DeviceType.Nyquist1, nq1Lower, "Should detect Nyquist1 from lowercase nq1");
        Assert.AreEqual(DeviceType.Nyquist1, nq1Mixed, "Should detect Nyquist1 from mixed case Nq1");
    }

    [TestMethod]
    public void DetectFromPartNumber_ShouldReturnUnknown_ForUnrecognizedPartNumber()
    {
        // Arrange & Act
        var deviceType = DeviceTypeDetector.DetectFromPartNumber("UnknownDevice");

        // Assert
        Assert.AreEqual(DeviceType.Unknown, deviceType, "Should default to Unknown for unrecognized part number");
    }

    [TestMethod]
    public void DetectFromPartNumber_ShouldReturnUnknown_ForEmptyString()
    {
        // Arrange & Act
        var deviceType = DeviceTypeDetector.DetectFromPartNumber("");

        // Assert
        Assert.AreEqual(DeviceType.Unknown, deviceType, "Should return Unknown for empty string");
    }

    [TestMethod]
    public void DetectFromPartNumber_ShouldReturnUnknown_ForNull()
    {
        // Arrange & Act
        var deviceType = DeviceTypeDetector.DetectFromPartNumber(null);

        // Assert
        Assert.AreEqual(DeviceType.Unknown, deviceType, "Should return Unknown for null");
    }

    [TestMethod]
    public void DetectFromPartNumber_ShouldReturnUnknown_ForWhitespace()
    {
        // Arrange & Act
        var deviceType = DeviceTypeDetector.DetectFromPartNumber("   ");

        // Assert
        Assert.AreEqual(DeviceType.Unknown, deviceType, "Should return Unknown for whitespace");
    }

    /// <summary>
    /// Test implementation of AbstractStreamingDevice for testing purposes
    /// </summary>
    private class TestStreamingDevice : AbstractStreamingDevice
    {
        public override ConnectionType ConnectionType => ConnectionType.Usb;

        public override bool Connect() => true;

        public override bool Disconnect() => true;

        public override bool Write(string command) => true;
    }
}
