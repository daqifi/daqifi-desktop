using Daqifi.Desktop.Device;
using Daqifi.Desktop.DataModel.Network;
using Daqifi.Desktop.IO.Messages;

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
        Assert.AreEqual(2, (int)DeviceType.Nyquist3, "Nyquist3 should have value 2");
    }

    [TestMethod]
    public void DeviceType_EnumNames_ShouldMatchExpectedValues()
    {
        // Arrange & Act & Assert
        Assert.AreEqual("Unknown", DeviceType.Unknown.ToString());
        Assert.AreEqual("Nyquist1", DeviceType.Nyquist1.ToString());
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

    /// <summary>
    /// Test implementation of AbstractStreamingDevice for testing purposes
    /// </summary>
    private class TestStreamingDevice : AbstractStreamingDevice
    {
        public override ConnectionType ConnectionType => ConnectionType.Usb;

        public override bool Connect() => true;

        public override bool Disconnect() => true;

        public override bool Write(string command) => true;

        // Expose protected methods for testing
        public DeviceType TestDetectByCapabilities(DaqifiOutMessage message)
        {
            return DetectByCapabilities(message);
        }

        public void TestHydrateDeviceMetadata(DaqifiOutMessage message)
        {
            HydrateDeviceMetadata(message);
        }
    }

    #region Capability Detection Tests

    [TestMethod]
    public void DetectByCapabilities_Nyquist3_ShouldReturnNyquist3()
    {
        // Arrange
        var device = new TestStreamingDevice();
        var message = CreateTestMessage();
        message.AnalogInPortNum = 8;   // Nyquist 3 has 8 analog in
        message.AnalogOutPortNum = 16; // Nyquist 3 has 16 analog out

        // Act
        var result = device.TestDetectByCapabilities(message);

        // Assert
        Assert.AreEqual(DeviceType.Nyquist3, result, "Should detect Nyquist3 with 8 AI and 16 AO ports");
    }

    [TestMethod]
    public void DetectByCapabilities_Nyquist1_ShouldReturnNyquist1()
    {
        // Arrange
        var device = new TestStreamingDevice();
        var message = CreateTestMessage();
        message.AnalogInPortNum = 16;  // Nyquist 1 has 16 analog in
        message.AnalogOutPortNum = 0;  // Nyquist 1 has 0 analog out

        // Act
        var result = device.TestDetectByCapabilities(message);

        // Assert
        Assert.AreEqual(DeviceType.Nyquist1, result, "Should detect Nyquist1 with 16 AI and 0 AO ports");
    }

    [TestMethod]
    public void DetectByCapabilities_UnknownConfiguration_ShouldReturnUnknown()
    {
        // Arrange
        var device = new TestStreamingDevice();
        var message = CreateTestMessage();
        message.AnalogInPortNum = 4;   // Unknown configuration
        message.AnalogOutPortNum = 2;  // Unknown configuration

        // Act
        var result = device.TestDetectByCapabilities(message);

        // Assert
        Assert.AreEqual(DeviceType.Unknown, result, "Should return Unknown for unrecognized configuration");
    }

    [TestMethod]
    public void DetectByCapabilities_EdgeCase_ZeroPorts_ShouldReturnUnknown()
    {
        // Arrange
        var device = new TestStreamingDevice();
        var message = CreateTestMessage();
        message.AnalogInPortNum = 0;   // Edge case
        message.AnalogOutPortNum = 0;  // Edge case

        // Act
        var result = device.TestDetectByCapabilities(message);

        // Assert
        Assert.AreEqual(DeviceType.Unknown, result, "Should return Unknown for zero ports configuration");
    }

    [TestMethod]
    public void DetectByCapabilities_PartialMatch_ShouldReturnUnknown()
    {
        // Arrange - Test partial matches that shouldn't trigger detection
        var device = new TestStreamingDevice();
        var testCases = new[]
        {
            new { AI = 8, AO = 0, Description = "8 AI, 0 AO (partial Nyquist3)" },
            new { AI = 16, AO = 16, Description = "16 AI, 16 AO (partial match)" },
            new { AI = 0, AO = 16, Description = "0 AI, 16 AO (partial Nyquist3)" }
        };

        foreach (var testCase in testCases)
        {
            // Arrange
            var message = CreateTestMessage();
            message.AnalogInPortNum = (uint)testCase.AI;
            message.AnalogOutPortNum = (uint)testCase.AO;

            // Act
            var result = device.TestDetectByCapabilities(message);

            // Assert
            Assert.AreEqual(DeviceType.Unknown, result, $"Should return Unknown for {testCase.Description}");
        }
    }

    [TestMethod]
    public void HydrateDeviceMetadata_Integration_ShouldSetDeviceTypeAutomatically()
    {
        // Arrange
        var device = new TestStreamingDevice();
        var message = CreateTestMessage();
        message.AnalogInPortNum = 8;   // Nyquist 3 configuration
        message.AnalogOutPortNum = 16;

        // Verify initial state
        Assert.AreEqual(DeviceType.Unknown, device.DeviceType, "Device should start with Unknown type");

        // Act - Call the actual method that would be called during device initialization
        device.TestHydrateDeviceMetadata(message);

        // Assert
        Assert.AreEqual(DeviceType.Nyquist3, device.DeviceType, "Device type should be automatically detected and set to Nyquist3");
    }

    [TestMethod]
    public void HydrateDeviceMetadata_Integration_Nyquist1_ShouldSetCorrectType()
    {
        // Arrange
        var device = new TestStreamingDevice();
        var message = CreateTestMessage();
        message.AnalogInPortNum = 16;  // Nyquist 1 configuration
        message.AnalogOutPortNum = 0;

        // Act
        device.TestHydrateDeviceMetadata(message);

        // Assert
        Assert.AreEqual(DeviceType.Nyquist1, device.DeviceType, "Device type should be automatically set to Nyquist1");
    }

    [TestMethod]
    public void HydrateDeviceMetadata_Integration_UnknownConfig_ShouldRemainUnknown()
    {
        // Arrange
        var device = new TestStreamingDevice();
        var message = CreateTestMessage();
        message.AnalogInPortNum = 4;   // Unknown configuration
        message.AnalogOutPortNum = 2;

        // Act
        device.TestHydrateDeviceMetadata(message);

        // Assert
        Assert.AreEqual(DeviceType.Unknown, device.DeviceType, "Device type should remain Unknown for unrecognized configuration");
    }

    #endregion

    private DaqifiOutMessage CreateTestMessage()
    {
        return new DaqifiOutMessage
        {
            MsgTimeStamp = 1000,
            DeviceSn = 12345,
            DeviceFwRev = "1.0.0"
        };
    }
}
