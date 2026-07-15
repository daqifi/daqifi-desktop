using System.Linq;
using Daqifi.Desktop.Channel;
using ChannelType = Daqifi.Core.Channel.ChannelType;
using Daqifi.Desktop.Device;
using Daqifi.Core.Communication.Messages;
using Daqifi.Core.Device;
using CoreStreamingDevice = Daqifi.Core.Device.DaqifiStreamingDevice;

namespace Daqifi.Desktop.Test.Device;

/// <summary>
/// Verifies analog stream decode maps device data to the correct channel regardless of which
/// subset of channels is active (issue #663 predecessor coverage). Channel decoding itself is
/// delegated to Core's <c>DaqifiStreamingDevice</c> (issue #613), so these tests route frames
/// through both halves of the real production pipeline: the desktop's own gating/dispatch
/// (<c>HandleInboundMessage</c>) and Core's decode step
/// (<see cref="TestCoreStreamingDevice.SimulateStreamFrame"/>) — in that synchronous order, exactly
/// as Core's actual <c>DaqifiStreamingDevice.OnStreamMessageReceived</c> sequences them in
/// production.
/// </summary>
[TestClass]
public class ChannelDataMappingTests
{
    // Large enough to cover every analog channel index exercised below.
    private const int ANALOG_PORT_COUNT = 3;

    private ChannelMappingTestDevice _device;

    [TestInitialize]
    public void Setup()
    {
        _device = new ChannelMappingTestDevice(ANALOG_PORT_COUNT);
    }

    private AnalogChannel ConfigureAnalogChannel(int index, bool isActive = true)
    {
        var channel = (AnalogChannel)_device.DataChannels.Single(
            c => c.Type == ChannelType.Analog && c.Index == index);
        channel.IsActive = isActive;
        return channel;
    }

    private void RouteMessage(DaqifiOutMessage message) => _device.RouteStreamFrame(message);

    [TestMethod]
    public void SingleChannel_AI1_ShouldReceiveCorrectData()
    {
        // Arrange
        var channel1 = ConfigureAnalogChannel(1); // Only AI1 is active

        var message = CreateTestMessage();
        message.AnalogInDataFloat.Add(5); // Device sends 5V for the single active channel

        // Act
        RouteMessage(message);

        // Assert
        Assert.IsNotNull(channel1.ActiveSample, "AI1 should receive data when it's the only active channel");
        Assert.AreEqual(5.0, channel1.ActiveSample.Value, 0.01, "AI1 should show 5V");
    }

    [TestMethod]
    public void SingleChannel_AI2_ShouldReceiveCorrectData()
    {
        // Arrange
        var channel2 = ConfigureAnalogChannel(2); // Only AI2 is active

        var message = CreateTestMessage();
        message.AnalogInDataFloat.Add(5); // Device sends 5V for the single active channel

        // Act
        RouteMessage(message);

        // Assert
        Assert.IsNotNull(channel2.ActiveSample, "AI2 should receive data when it's the only active channel");
        Assert.AreEqual(5.0, channel2.ActiveSample.Value, 0.01, "AI2 should show 5V");
    }

    [TestMethod]
    public void MultipleChannels_AI0AndAI1_ShouldReceiveCorrectData()
    {
        // Arrange
        var channel0 = ConfigureAnalogChannel(0);
        var channel1 = ConfigureAnalogChannel(1);

        var message = CreateTestMessage();
        message.AnalogInDataFloat.Add(5); // Data for first active channel (AI0)
        message.AnalogInDataFloat.Add(3); // Data for second active channel (AI1)

        // Act
        RouteMessage(message);

        // Assert
        Assert.IsNotNull(channel0.ActiveSample, "AI0 should receive data");
        Assert.AreEqual(5.0, channel0.ActiveSample.Value, 0.01, "AI0 should show 5V");

        Assert.IsNotNull(channel1.ActiveSample, "AI1 should receive data");
        Assert.AreEqual(3.0, channel1.ActiveSample.Value, 0.01, "AI1 should show 3V");
    }

    [TestMethod]
    public void MultipleChannels_AI0AndAI2_ShouldReceiveCorrectData()
    {
        // Arrange — activating a non-consecutive subset (skipping AI1)
        var channel0 = ConfigureAnalogChannel(0);
        var channel2 = ConfigureAnalogChannel(2);

        var message = CreateTestMessage();
        message.AnalogInDataFloat.Add(5); // Data for first active channel (AI0)
        message.AnalogInDataFloat.Add(3); // Data for second active channel (AI2)

        // Act
        RouteMessage(message);

        // Assert
        Assert.IsNotNull(channel0.ActiveSample, "AI0 should receive data");
        Assert.AreEqual(5.0, channel0.ActiveSample.Value, 0.01, "AI0 should show 5V");

        Assert.IsNotNull(channel2.ActiveSample, "AI2 should receive data");
        Assert.AreEqual(3.0, channel2.ActiveSample.Value, 0.01, "AI2 should show 3V");
    }

    [TestMethod]
    public void ChannelOrderingAssumption_ShouldMatchDeviceDataOrder()
    {
        // Arrange - Activate channels in reverse order: AI2, AI1, AI0
        var channel2 = ConfigureAnalogChannel(2);
        var channel1 = ConfigureAnalogChannel(1);
        var channel0 = ConfigureAnalogChannel(0);

        var message = CreateTestMessage();
        // The device sends data in channel index order (0, 1, 2), not activation order
        message.AnalogInDataFloat.Add(1); // Should go to AI0 (index 0)
        message.AnalogInDataFloat.Add(2); // Should go to AI1 (index 1)
        message.AnalogInDataFloat.Add(3); // Should go to AI2 (index 2)

        // Act
        RouteMessage(message);

        // Assert
        Assert.IsNotNull(channel0.ActiveSample, "AI0 should receive data");
        Assert.AreEqual(1.0, channel0.ActiveSample.Value, 0.01, "AI0 should receive first data value (1.0V)");

        Assert.IsNotNull(channel1.ActiveSample, "AI1 should receive data");
        Assert.AreEqual(2.0, channel1.ActiveSample.Value, 0.01, "AI1 should receive second data value (2.0V)");

        Assert.IsNotNull(channel2.ActiveSample, "AI2 should receive data");
        Assert.AreEqual(3.0, channel2.ActiveSample.Value, 0.01, "AI2 should receive third data value (3.0V)");
    }

    [TestMethod]
    public void InactiveChannel_GetsNoSample()
    {
        // Arrange — activate AI1 and AI2 (skipping AI0), which stays inactive
        var channel0 = ConfigureAnalogChannel(0, isActive: false);
        ConfigureAnalogChannel(1);
        ConfigureAnalogChannel(2);

        var message = CreateTestMessage();
        message.AnalogInDataFloat.Add(2); // AI1
        message.AnalogInDataFloat.Add(3); // AI2

        // Act
        RouteMessage(message);

        // Assert
        Assert.IsNull(channel0.ActiveSample, "An inactive channel must not receive a sample");
    }

    [TestMethod]
    public void FloatData_SingleChannel_AI0_ShouldReceivePreScaledValue()
    {
        // USB firmware sends AnalogInDataFloat (pre-scaled volts) instead of AnalogInData (raw ADC counts)
        // Arrange
        var channel0 = ConfigureAnalogChannel(0);

        var message = CreateTestMessage();
        message.AnalogInDataFloat.Add(2.75f); // Pre-scaled: 2.75 V already

        // Act
        RouteMessage(message);

        // Assert
        Assert.IsNotNull(channel0.ActiveSample, "AI0 should receive data from AnalogInDataFloat");
        Assert.AreEqual(2.75, channel0.ActiveSample.Value, 0.001, "AI0 should show pre-scaled float value directly");
    }

    [TestMethod]
    public void FloatData_MultipleChannels_ShouldReceivePreScaledValues()
    {
        // Arrange
        var channel0 = ConfigureAnalogChannel(0);
        var channel1 = ConfigureAnalogChannel(1);

        var message = CreateTestMessage();
        message.AnalogInDataFloat.Add(1.1f); // AI0
        message.AnalogInDataFloat.Add(3.3f); // AI1

        // Act
        RouteMessage(message);

        // Assert
        Assert.IsNotNull(channel0.ActiveSample, "AI0 should receive data");
        Assert.AreEqual(1.1, channel0.ActiveSample.Value, 0.001, "AI0 should show 1.1V");

        Assert.IsNotNull(channel1.ActiveSample, "AI1 should receive data");
        Assert.AreEqual(3.3, channel1.ActiveSample.Value, 0.001, "AI1 should show 3.3V");
    }

    [TestMethod]
    public void RawData_AppliesChannelCalibration()
    {
        // Arrange — WiFi firmware sends raw ADC counts; Core applies each channel's calibration.
        var channel0 = ConfigureAnalogChannel(0);

        var message = CreateTestMessage();
        message.AnalogInData.Add(2048); // Half of the 4095 resolution configured below

        // Act
        RouteMessage(message);

        // Assert — resolution 4095, PortRange 5.0, CalibrationM/InternalScaleM 1.0, CalibrationB 0.0
        // (see ChannelMappingTestDevice's status message), so 2048/4095*5.0 ≈ 2.5003V
        Assert.IsNotNull(channel0.ActiveSample, "AI0 should receive calibrated data");
        Assert.AreEqual(2.5, channel0.ActiveSample.Value, 0.01, "AI0 should apply channel calibration to the raw count");
    }

    private static DaqifiOutMessage CreateTestMessage()
    {
        return new DaqifiOutMessage
        {
            MsgTimeStamp = 1000,
            DeviceSn = 12345,
            DeviceFwRev = "1.0.0"
        };
    }

    /// <summary>
    /// Test device exposing the real inbound-message pipeline (protocol handler → stream
    /// routing → Core decode → per-channel SampleReceived → desktop DataSample mapping).
    /// </summary>
    private sealed class ChannelMappingTestDevice : AbstractStreamingDevice
    {
        private readonly TestCoreStreamingDevice _coreDevice;

        public ChannelMappingTestDevice(int analogPortCount)
        {
            _coreDevice = new TestCoreStreamingDevice();
            _coreDevice.Connect();

            var statusMessage = new DaqifiOutMessage
            {
                DevicePn = "Nq1",
                DeviceSn = 12345,
                DeviceFwRev = "1.0.0",
                AnalogInPortNum = (uint)analogPortCount,
                AnalogInRes = 4095,
            };
            for (var i = 0; i < analogPortCount; i++)
            {
                statusMessage.AnalogInCalM.Add(1.0f);
                statusMessage.AnalogInCalB.Add(0.0f);
                statusMessage.AnalogInIntScaleM.Add(1.0f);
                statusMessage.AnalogInPortRange.Add(5.0f);
            }

            _coreDevice.Metadata.UpdateFromProtobuf(statusMessage);
            _coreDevice.PopulateChannelsFromStatus(statusMessage);

            // Wires the desktop AnalogChannel wrappers around Core's actual channel instances
            // (including the SampleReceived subscription installed by SyncChannelsFromCore).
            SyncFromCoreDevice(_coreDevice);

            InitializeDeviceState();

            _coreDevice.StreamingFrequency = 1;
            _coreDevice.StartStreaming();
            IsStreaming = true;
        }

        public override ConnectionType ConnectionType => ConnectionType.Usb;

        protected override CoreStreamingDevice? CoreDeviceForStreaming => _coreDevice;

        public override bool Connect() => true;

        public override bool Disconnect() => true;

        public override bool Write(string command) => true;

        protected override void SendMessage(IOutboundMessage<string> message)
        {
        }

        protected override void DispatchDeviceMessage(DeviceMessage deviceMessage)
        {
            // The logging pipeline needs the application service provider, which unit tests don't have.
        }

        /// <summary>
        /// Routes a raw frame through both halves of the real production pipeline: the desktop's
        /// own gating/dispatch (<c>HandleInboundMessage</c>) and Core's decode step
        /// (<see cref="TestCoreStreamingDevice.SimulateStreamFrame"/>) — in that order, exactly as
        /// Core's actual <c>DaqifiStreamingDevice.OnStreamMessageReceived</c> sequences them in
        /// production (base call raises <c>MessageReceived</c> before Core decodes).
        /// </summary>
        public void RouteStreamFrame(DaqifiOutMessage message)
        {
            HandleInboundMessage(
                new MessageReceivedEventArgs(
                    new GenericInboundMessage<object>(message)));
            _coreDevice.SimulateStreamFrame(message);
        }
    }

    private sealed class TestCoreStreamingDevice() : CoreStreamingDevice("TestDevice")
    {
        public override void Send<T>(IOutboundMessage<T> message)
        {
        }

        public void SimulateStreamFrame(DaqifiOutMessage message) => OnStreamMessageReceived(message);
    }
}
