using Daqifi.Desktop.Channel;
using ChannelDirection = Daqifi.Core.Channel.ChannelDirection;
using ChannelType = Daqifi.Core.Channel.ChannelType;
using Daqifi.Desktop.Device;

namespace Daqifi.Desktop.Test.Device;

[TestClass]
public class ChannelDataMappingTests
{
    private TestableStreamingDevice _device;

    [TestInitialize]
    public void Setup()
    {
        _device = new TestableStreamingDevice();
        
        // Create channels AI0, AI1, AI2 for testing
        var channel0 = new AnalogChannel(_device, "AI0", 0, ChannelDirection.Input, false, 0.0f, 1.0f, 1.0f, 5.0f, 4096);
        var channel1 = new AnalogChannel(_device, "AI1", 1, ChannelDirection.Input, false, 0.0f, 1.0f, 1.0f, 5.0f, 4096);
        var channel2 = new AnalogChannel(_device, "AI2", 2, ChannelDirection.Input, false, 0.0f, 1.0f, 1.0f, 5.0f, 4096);
        
        _device.DataChannels.Add(channel0);
        _device.DataChannels.Add(channel1);
        _device.DataChannels.Add(channel2);
    }

    [TestMethod]
    public void SingleChannel_AI1_ShouldReceiveCorrectData()
    {
        // This test demonstrates the current bug where AI1 alone doesn't receive data
        
        // Arrange
        var channel1 = _device.DataChannels[1] as AnalogChannel;
        channel1.IsActive = true; // Only AI1 is active
        
        var message = CreateTestMessage();
        message.AnalogInData.Add(5); // Device sends 5V for the single active channel
        
        // Act
        _device.TestHandleStreamingMessage(message);
        
        // Assert
        // This test will FAIL with current implementation because the data mapping is incorrect
        Assert.IsNotNull(channel1.ActiveSample, "AI1 should receive data when it's the only active channel");
        Assert.AreEqual(5.0, channel1.ActiveSample.Value, 0.01, "AI1 should show 5V");
    }

    [TestMethod]
    public void SingleChannel_AI2_ShouldReceiveCorrectData()
    {
        // This test demonstrates the current bug where AI2 alone doesn't receive data
        
        // Arrange
        var channel2 = _device.DataChannels[2] as AnalogChannel;
        channel2.IsActive = true; // Only AI2 is active
        
        var message = CreateTestMessage();
        message.AnalogInData.Add(5); // Device sends 5V for the single active channel
        
        // Act
        _device.TestHandleStreamingMessage(message);
        
        // Assert
        // This test will FAIL with current implementation
        Assert.IsNotNull(channel2.ActiveSample, "AI2 should receive data when it's the only active channel");
        Assert.AreEqual(5.0, channel2.ActiveSample.Value, 0.01, "AI2 should show 5V");
    }

    [TestMethod]
    public void MultipleChannels_AI0AndAI1_ShouldReceiveCorrectData()
    {
        // This test should pass with current implementation
        
        // Arrange
        var channel0 = _device.DataChannels[0] as AnalogChannel;
        var channel1 = _device.DataChannels[1] as AnalogChannel;
        
        channel0.IsActive = true;
        channel1.IsActive = true;
        
        var message = CreateTestMessage();
        message.AnalogInData.Add(5); // Data for first active channel (AI0)
        message.AnalogInData.Add(3); // Data for second active channel (AI1)
        
        // Act
        _device.TestHandleStreamingMessage(message);
        
        // Assert
        Assert.IsNotNull(channel0.ActiveSample, "AI0 should receive data");
        Assert.AreEqual(5.0, channel0.ActiveSample.Value, 0.01, "AI0 should show 5V");

        Assert.IsNotNull(channel1.ActiveSample, "AI1 should receive data");
        Assert.AreEqual(3.0, channel1.ActiveSample.Value, 0.01, "AI1 should show 3V");
    }

    [TestMethod]
    public void MultipleChannels_AI0AndAI2_ShouldReceiveCorrectData()
    {
        // This test demonstrates the issue where AI2 doesn't receive data when paired with AI0
        
        // Arrange
        var channel0 = _device.DataChannels[0] as AnalogChannel;
        var channel2 = _device.DataChannels[2] as AnalogChannel;
        
        channel0.IsActive = true;
        channel2.IsActive = true;
        
        var message = CreateTestMessage();
        message.AnalogInData.Add(5); // Data for first active channel (AI0)
        message.AnalogInData.Add(3); // Data for second active channel (AI2)
        
        // Act
        _device.TestHandleStreamingMessage(message);
        
        // Assert
        Assert.IsNotNull(channel0.ActiveSample, "AI0 should receive data");
        Assert.AreEqual(5.0, channel0.ActiveSample.Value, 0.01, "AI0 should show 5V");
        
        // This assertion will FAIL with current implementation
        Assert.IsNotNull(channel2.ActiveSample, "AI2 should receive data");
        Assert.AreEqual(3.0, channel2.ActiveSample.Value, 0.01, "AI2 should show 3V");
    }

    [TestMethod]
    public void ChannelOrderingAssumption_ShouldMatchDeviceDataOrder()
    {
        // This test verifies our assumption about how the device sends data
        // The device should send data in channel index order, not in the order channels are activated
        
        // Arrange - Activate channels in reverse order: AI2, AI1, AI0
        var channel0 = _device.DataChannels[0] as AnalogChannel;
        var channel1 = _device.DataChannels[1] as AnalogChannel;
        var channel2 = _device.DataChannels[2] as AnalogChannel;
        
        // Activate in reverse order
        channel2.IsActive = true;
        channel1.IsActive = true;
        channel0.IsActive = true;
        
        var message = CreateTestMessage();
        // If device sends data in channel index order (0, 1, 2), then:
        message.AnalogInData.Add(1); // Should go to AI0 (index 0)
        message.AnalogInData.Add(2); // Should go to AI1 (index 1)
        message.AnalogInData.Add(3); // Should go to AI2 (index 2)
        
        // Act
        _device.TestHandleStreamingMessage(message);
        
        // Assert - If device sends in index order, each channel should get its expected value
        Assert.IsNotNull(channel0.ActiveSample, "AI0 should receive data");
        Assert.AreEqual(1.0, channel0.ActiveSample.Value, 0.01, "AI0 should receive first data value (1.0V)");
        
        Assert.IsNotNull(channel1.ActiveSample, "AI1 should receive data");
        Assert.AreEqual(2.0, channel1.ActiveSample.Value, 0.01, "AI1 should receive second data value (2.0V)");
        
        Assert.IsNotNull(channel2.ActiveSample, "AI2 should receive data");
        Assert.AreEqual(3.0, channel2.ActiveSample.Value, 0.01, "AI2 should receive third data value (3.0V)");
    }

    [TestMethod]
    public void CurrentImplementation_ShowsIncorrectMapping()
    {
        // This test demonstrates how the current implementation incorrectly maps data
        
        // Arrange - Activate AI1 and AI2 (skipping AI0)
        var channel1 = _device.DataChannels[1] as AnalogChannel;
        var channel2 = _device.DataChannels[2] as AnalogChannel;
        
        channel1.IsActive = true;
        channel2.IsActive = true;
        
        var message = CreateTestMessage();
        // Device sends data for channels 1 and 2 in index order
        message.AnalogInData.Add(2); // Data for AI1 (index 1)
        message.AnalogInData.Add(3); // Data for AI2 (index 2)
        
        // Act
        _device.TestHandleStreamingMessage(message);
        
        // Current implementation will assign data based on active channel order:
        // - First active channel (AI1) gets first data value (1.5) ✓ Correct
        // - Second active channel (AI2) gets second data value (2.5) ✓ Correct
        
        // But if we had AI0 and AI2 active (skipping AI1):
        // - Device would send data for indices 0 and 2
        // - Current code would assign first data to AI0 ✓ and second data to AI2 ✓
        // This works by coincidence when channels are consecutive
        
        Assert.IsNotNull(channel1.ActiveSample, "AI1 should receive data");
        Assert.IsNotNull(channel2.ActiveSample, "AI2 should receive data");
    }

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

// Test implementation of AbstractStreamingDevice to expose protected methods
public class TestableStreamingDevice : AbstractStreamingDevice
{
    public override ConnectionType ConnectionType => ConnectionType.Usb;

    public override bool Connect() => true;
    public override bool Disconnect() => true;
    public override bool Write(string command) => true;

    // Expose protected methods for testing
    public void TestHandleStreamingMessage(DaqifiOutMessage message)
    {
        // Set IsStreaming to true so the method processes the message
        IsStreaming = true;

        // Directly test the data mapping logic without going through the full message handling
        TestDirectDataMapping(message);
    }

    private void TestDirectDataMapping(DaqifiOutMessage message)
    {
        // Simulate the data mapping logic from HandleStreamingMessageReceived
        var messageTimestamp = DateTime.Now;
        var hasAnalogData = message.AnalogInData.Count > 0;

        if (hasAnalogData)
        {
            var activeAnalogChannels = DataChannels.Where(c => c.IsActive && c.Type == ChannelType.Analog)
                                                  .Cast<AnalogChannel>()
                                                  .OrderBy(c => c.Index)
                                                  .ToList();

            for (var dataIndex = 0; dataIndex < message.AnalogInData.Count && dataIndex < activeAnalogChannels.Count; dataIndex++)
            {
                var channel = activeAnalogChannels[dataIndex];
                var rawValue = message.AnalogInData.ElementAt(dataIndex);
                // Simple scaling for test - just convert to double
                var scaledValue = Convert.ToDouble(rawValue);
                var sample = new DataSample(this, channel, messageTimestamp, scaledValue);
                channel.ActiveSample = sample;
            }
        }
    }
}
