using Daqifi.Desktop.Device;
using Daqifi.Desktop.Channel;
using Daqifi.Core.Device.Network;
using Daqifi.Core.Communication.Messages;
using Daqifi.Core.Communication.Producers;
using Daqifi.Core.Device; // Added for DeviceType, DeviceTypeDetector from Core
using Daqifi.Desktop.IO.Messages;
using Moq;
using System.Threading;
using System.Windows.Media;
using CoreStreamingDevice = Daqifi.Core.Device.DaqifiStreamingDevice;

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

    [TestMethod]
    public async Task UpdateNetworkConfiguration_WhenDisconnected_ThrowsInvalidOperationException()
    {
        // Arrange
        var device = new TestStreamingDevice();

        try
        {
            await device.UpdateNetworkConfiguration();
            Assert.Fail("Expected UpdateNetworkConfiguration to throw when the device is disconnected.");
        }
        catch (InvalidOperationException exception)
        {
            Assert.AreEqual("Device is not connected.", exception.Message);
        }
    }

    [TestMethod]
    public async Task UpdateNetworkConfiguration_WhenStreamingAndDisconnected_ThrowsWithoutStoppingStreaming()
    {
        // Arrange
        var device = new TestStreamingDevice
        {
            IsStreaming = true
        };

        try
        {
            await device.UpdateNetworkConfiguration();
            Assert.Fail("Expected UpdateNetworkConfiguration to throw when the device is disconnected.");
        }
        catch (InvalidOperationException exception)
        {
            Assert.AreEqual("Device is not connected.", exception.Message);
        }

        Assert.IsTrue(device.IsStreaming, "Streaming state should be preserved when the Core device is not connected.");
        Assert.AreEqual(0, device.SentCommands.Count, "No commands should be sent when the Core device is not connected.");
    }

    [TestMethod]
    public async Task UpdateNetworkConfiguration_WhenStreaming_StopsStreamingBeforeDelegatingToCore()
    {
        // Arrange
        var device = new NetworkConfigurationTestDevice();
        device.NetworkConfiguration = new NetworkConfiguration(
            WifiMode.ExistingNetwork,
            WifiSecurityType.WpaPskPhrase,
            "TestNetwork",
            "TestPassword");
        device.IsStreaming = true;

        // Act
        await device.UpdateNetworkConfiguration();

        // Assert
        Assert.IsFalse(device.IsStreaming, "Desktop streaming state should be reset before delegating to Core.");
        Assert.AreEqual(
            $"core:{ScpiMessageProducer.StopStreaming.Data}",
            device.SentCommands[0],
            "StopStreaming should be the first command sent via the Core layer.");
        Assert.IsTrue(
            device.SentCommands.Contains($"core:{ScpiMessageProducer.SetNetworkWifiModeExisting.Data}"),
            "Core should own the network configuration command sequence.");
        Assert.IsFalse(
            device.SentCommands.Contains($"desktop:{ScpiMessageProducer.SetNetworkWifiModeExisting.Data}"),
            "Desktop should no longer duplicate the network configuration command sequence.");
    }

    [TestMethod]
    public async Task UpdateNetworkConfiguration_WhenNotStreaming_DelegatesWithoutSendingStopStreaming()
    {
        // Arrange
        var device = new NetworkConfigurationTestDevice();
        device.NetworkConfiguration = new NetworkConfiguration(
            WifiMode.ExistingNetwork,
            WifiSecurityType.WpaPskPhrase,
            "TestNetwork",
            "TestPassword");

        // Act
        await device.UpdateNetworkConfiguration();

        // Assert
        Assert.IsFalse(
            device.SentCommands.Contains($"desktop:{ScpiMessageProducer.StopStreaming.Data}"),
            "Desktop should not send StopStreaming when it was not streaming.");
        Assert.IsTrue(
            device.SentCommands.Contains($"core:{ScpiMessageProducer.SetNetworkWifiModeExisting.Data}"),
            "Core should still receive the full network configuration command sequence.");
    }

    [TestMethod]
    public async Task UpdateNetworkConfiguration_WhenWifi_DoesNotRestoreSdInterface()
    {
        // Arrange
        var device = new NetworkConfigurationTestDevice(connectionType: ConnectionType.Wifi);
        device.NetworkConfiguration = new NetworkConfiguration(
            WifiMode.ExistingNetwork,
            WifiSecurityType.WpaPskPhrase,
            "TestNetwork",
            "TestPassword");

        // Act
        await device.UpdateNetworkConfiguration();

        // Assert
        Assert.IsFalse(
            device.SentCommands.Contains($"desktop:{ScpiMessageProducer.DisableNetworkLan.Data}"),
            "Desktop should not disable LAN for a WiFi device after a network update.");
        Assert.IsFalse(
            device.SentCommands.Contains($"desktop:{ScpiMessageProducer.EnableStorageSd.Data}"),
            "Desktop should not re-enable SD for a WiFi device; it shares no SPI bus with the desktop transport.");
    }

    [TestMethod]
    public async Task UpdateNetworkConfiguration_WhenInLogToDevice_RestoresSdInterfaceAfterCoreUpdate()
    {
        // Arrange
        var device = new NetworkConfigurationTestDevice();
        device.NetworkConfiguration = new NetworkConfiguration(
            WifiMode.SelfHosted,
            WifiSecurityType.None,
            "DAQiFi_Device",
            string.Empty);
        device.SwitchMode(DeviceMode.LogToDevice);
        device.SentCommands.Clear();

        // Act
        await device.UpdateNetworkConfiguration();

        // Assert
        CollectionAssert.AreEqual(
            new[]
            {
                $"core:{ScpiMessageProducer.SaveNetworkLan.Data}",
                $"desktop:{ScpiMessageProducer.DisableNetworkLan.Data}",
                $"desktop:{ScpiMessageProducer.EnableStorageSd.Data}",
                $"desktop:{ScpiMessageProducer.SetStreamInterface(Daqifi.Core.Communication.StreamInterface.SdCard).Data}"
            },
            device.SentCommands.TakeLast(4).ToArray(),
            "Desktop should restore the full SD interface after the Core network update when the device is in LogToDevice mode.");
    }

    [TestMethod]
    public async Task UpdateNetworkConfiguration_WhenCoreUpdateThrowsInLogToDevice_RestoresSdInterface()
    {
        // Arrange
        var device = new NetworkConfigurationTestDevice(throwOnCommandData: ScpiMessageProducer.SaveNetworkLan.Data);
        device.NetworkConfiguration = new NetworkConfiguration(
            WifiMode.SelfHosted,
            WifiSecurityType.None,
            "DAQiFi_Device",
            string.Empty);
        device.SwitchMode(DeviceMode.LogToDevice);
        device.SentCommands.Clear();

        try
        {
            await device.UpdateNetworkConfiguration();
            Assert.Fail("Expected the Core update to throw.");
        }
        catch (InvalidOperationException exception)
        {
            Assert.AreEqual("Injected test failure.", exception.Message);
        }

        CollectionAssert.AreEqual(
            new[]
            {
                $"desktop:{ScpiMessageProducer.DisableNetworkLan.Data}",
                $"desktop:{ScpiMessageProducer.EnableStorageSd.Data}",
                $"desktop:{ScpiMessageProducer.SetStreamInterface(Daqifi.Core.Communication.StreamInterface.SdCard).Data}"
            },
            device.SentCommands.TakeLast(3).ToArray(),
            "Desktop should restore the full SD interface even when the Core network update fails.");
    }

    [TestMethod]
    public void SyncFromCoreDevice_ReusesExistingDesktopChannelsAndPreservesDesktopState()
    {
        // Arrange
        var device = new CoreSynchronizationTestDevice();
        var initialCoreDevice = BuildCoreDeviceSnapshot(
            firmwareVersion: "1.0.0",
            calibrationM: 1.5f);

        device.ApplyCoreSnapshot(initialCoreDevice);

        var analogChannel = device.DataChannels.OfType<AnalogChannel>().Single();
        var digitalChannel = device.DataChannels.OfType<DigitalChannel>().Single();
        analogChannel.ScaleExpression = "x * 2";
        analogChannel.IsScalingActive = true;
        analogChannel.IsVisible = false;
        analogChannel.ChannelColorBrush = Brushes.Orange;
        digitalChannel.IsActive = true;

        var refreshedCoreDevice = BuildCoreDeviceSnapshot(
            firmwareVersion: "2.0.0",
            calibrationM: 2.5f);

        // Act
        device.ApplyCoreSnapshot(refreshedCoreDevice);

        // Assert
        var refreshedAnalogChannel = device.DataChannels.OfType<AnalogChannel>().Single();
        var refreshedDigitalChannel = device.DataChannels.OfType<DigitalChannel>().Single();

        Assert.AreSame(analogChannel, refreshedAnalogChannel, "Analog channel wrapper should be preserved.");
        Assert.AreSame(digitalChannel, refreshedDigitalChannel, "Digital channel wrapper should be preserved.");
        Assert.AreEqual("x * 2", refreshedAnalogChannel.ScaleExpression);
        Assert.IsTrue(refreshedAnalogChannel.IsScalingActive);
        Assert.IsFalse(refreshedAnalogChannel.IsVisible);
        Assert.AreSame(Brushes.Orange, refreshedAnalogChannel.ChannelColorBrush);
        Assert.IsTrue(refreshedDigitalChannel.IsActive, "Desktop channel activation state should be preserved.");
        Assert.AreEqual(2.5d, refreshedAnalogChannel.CalibrationMValue, 0.001d, "Core calibration data should refresh.");
        Assert.AreEqual("2.0.0", device.DeviceVersion);
        Assert.AreEqual(DeviceType.Nyquist1, device.DeviceType);
        Assert.AreEqual(WifiSecurityType.None, device.NetworkConfiguration.SecurityType);
    }

    [TestMethod]
    public void OnCoreChannelsPopulated_BuildsDesktopChannelsFromCoreDevice()
    {
        // Arrange
        var device = new CoreSynchronizationTestDevice();
        var coreDevice = BuildCoreDeviceSnapshot(firmwareVersion: "1.0.0", calibrationM: 1.5f);

        // Act — simulate the ChannelsPopulated event
        device.SimulateChannelsPopulated(coreDevice);

        // Assert
        Assert.AreEqual(2, device.DataChannels.Count, "Should have 1 analog + 1 digital channel.");
        var analog = device.DataChannels.OfType<AnalogChannel>().Single();
        var digital = device.DataChannels.OfType<DigitalChannel>().Single();
        Assert.AreEqual("AI0", analog.Name);
        Assert.AreEqual("DIO0", digital.Name);
        Assert.AreEqual(1.5d, analog.CalibrationMValue, 0.001d);
        Assert.AreEqual("1.0.0", device.DeviceVersion);
    }

    [TestMethod]
    public void OnCoreChannelsPopulated_ReconnectRebuildsChannelsCorrectly()
    {
        // Arrange — first connection
        var device = new CoreSynchronizationTestDevice();
        var firstCoreDevice = BuildCoreDeviceSnapshot(firmwareVersion: "1.0.0", calibrationM: 1.0f);
        device.SimulateChannelsPopulated(firstCoreDevice);

        var firstAnalog = device.DataChannels.OfType<AnalogChannel>().Single();
        firstAnalog.ScaleExpression = "x * 10";

        // Simulate disconnect: clear channels (as the real devices do)
        device.DataChannels.Clear();
        Assert.AreEqual(0, device.DataChannels.Count);

        // Act — reconnect with new core device
        var secondCoreDevice = BuildCoreDeviceSnapshot(firmwareVersion: "2.0.0", calibrationM: 3.0f);
        device.SimulateChannelsPopulated(secondCoreDevice);

        // Assert — channels rebuilt from scratch (no ghost state from first connection)
        Assert.AreEqual(2, device.DataChannels.Count);
        var reconnectedAnalog = device.DataChannels.OfType<AnalogChannel>().Single();
        Assert.AreEqual(3.0d, reconnectedAnalog.CalibrationMValue, 0.001d);
        Assert.AreEqual("2.0.0", device.DeviceVersion);
        // Scale expression should NOT carry over after disconnect+reconnect
        Assert.AreNotEqual("x * 10", reconnectedAnalog.ScaleExpression,
            "Desktop-only state should not persist across disconnect/reconnect.");
    }

    [TestMethod]
    public void OnCoreChannelsPopulated_ChannelRefreshPreservesWrappersWhenNotDisconnected()
    {
        // Arrange — initial population
        var device = new CoreSynchronizationTestDevice();
        var coreDevice = BuildCoreDeviceSnapshot(firmwareVersion: "1.0.0", calibrationM: 1.0f);
        device.SimulateChannelsPopulated(coreDevice);

        var originalAnalog = device.DataChannels.OfType<AnalogChannel>().Single();
        originalAnalog.ScaleExpression = "x * 5";
        originalAnalog.IsScalingActive = true;

        // Act — same-session channel refresh (e.g., re-query device info)
        var refreshedCoreDevice = BuildCoreDeviceSnapshot(firmwareVersion: "1.0.0", calibrationM: 2.0f);
        device.SimulateChannelsPopulated(refreshedCoreDevice);

        // Assert — wrapper identity preserved, core calibration refreshed
        var refreshedAnalog = device.DataChannels.OfType<AnalogChannel>().Single();
        Assert.AreSame(originalAnalog, refreshedAnalog, "Same wrapper should be reused during refresh.");
        Assert.AreEqual("x * 5", refreshedAnalog.ScaleExpression, "Desktop expression should survive a refresh.");
        Assert.IsTrue(refreshedAnalog.IsScalingActive, "Desktop scaling flag should survive a refresh.");
        Assert.AreEqual(2.0d, refreshedAnalog.CalibrationMValue, 0.001d, "Core calibration should update.");
    }

    [TestMethod]
    public void OnCoreChannelsPopulated_IgnoresNonDaqifiDeviceSender()
    {
        // Arrange
        var device = new CoreSynchronizationTestDevice();

        // Act — fire with a non-DaqifiDevice sender
        device.SimulateChannelsPopulatedFromSender(
            sender: "not a device",
            new ChannelsPopulatedEventArgs(Array.Empty<Daqifi.Core.Channel.IChannel>().AsReadOnly(), 0, 0));

        // Assert — no channels should be created or modified
        Assert.AreEqual(0, device.DataChannels.Count);
    }

    [TestMethod]
    public void StartSdCardLogging_WhenSynchronizationContextIsBlocked_CompletesWithoutDeadlock()
    {
        // Arrange
        var device = new SdCardLoggingTestDevice();
        device.SwitchMode(DeviceMode.LogToDevice);
        Exception? capturedException = null;

        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());

            try
            {
                device.StartSdCardLogging();
            }
            catch (Exception exception)
            {
                capturedException = exception;
            }
        })
        {
            IsBackground = true
        };

        // Act
        thread.Start();
        var completed = thread.Join(TimeSpan.FromSeconds(5));

        // Assert
        Assert.IsTrue(completed, "StartSdCardLogging should not deadlock on a synchronization-context-bound thread.");
        Assert.IsNull(capturedException, capturedException?.ToString());
        Assert.IsTrue(device.IsLoggingToSdCard, "Desktop state should reflect the Core SD logging state after the call completes.");
        CollectionAssert.Contains(
            device.SentCommands,
            $"core:{ScpiMessageProducer.EnableStorageSd.Data}",
            "Core SD enable command should still be issued.");
    }

    [TestMethod]
    public void StartSdCardLogging_UsesCombinedAnalogMaskAndConfiguresDigitalPortsOnce()
    {
        var device = new SdCardLoggingTestDevice();
        device.DataChannels.Add(new AnalogChannel(device, BuildAnalogInputCoreChannel(0)) { IsActive = true });
        device.DataChannels.Add(new AnalogChannel(device, BuildAnalogInputCoreChannel(2)) { IsActive = true });
        device.DataChannels.Add(new AnalogChannel(device, BuildAnalogInputCoreChannel(4)) { IsActive = true });
        device.DataChannels.Add(new DigitalChannel(device, BuildDigitalInputCoreChannel(0)) { IsActive = true });
        device.DataChannels.Add(new DigitalChannel(device, BuildDigitalInputCoreChannel(1)) { IsActive = true });
        device.SwitchMode(DeviceMode.LogToDevice);

        device.StartSdCardLogging();

        Assert.AreEqual(
            1,
            device.SentCommands.Count(command => command == $"desktop:{ScpiMessageProducer.EnableDioPorts().Data}"),
            "Desktop should enable digital ports once before starting SD logging.");
        CollectionAssert.DoesNotContain(
            device.SentCommands,
            $"desktop:{ScpiMessageProducer.EnableAdcChannels("1").Data}",
            "Desktop should not send per-channel analog enable commands during SD logging startup.");
        CollectionAssert.Contains(
            device.SentCommands,
            $"core:{ScpiMessageProducer.EnableAdcChannels("10101").Data}",
            "Core should receive a single combined binary analog mask for the active SD logging channels.");
    }

    [TestMethod]
    public void SwitchMode_WhenEnteringLogToDevice_SetsSdCardStreamInterface()
    {
        var device = new TestStreamingDevice();

        device.SwitchMode(DeviceMode.LogToDevice);

        CollectionAssert.AreEqual(
            new[]
            {
                ScpiMessageProducer.DisableNetworkLan.Data,
                ScpiMessageProducer.EnableStorageSd.Data,
                ScpiMessageProducer.SetStreamInterface(Daqifi.Core.Communication.StreamInterface.SdCard).Data
            },
            device.SentCommands);
    }

    [TestMethod]
    public void SwitchMode_WhenReturningToStreamToApp_SetsUsbStreamInterface()
    {
        var device = new TestStreamingDevice();
        device.SwitchMode(DeviceMode.LogToDevice);
        device.SentCommands.Clear();

        device.SwitchMode(DeviceMode.StreamToApp);

        CollectionAssert.AreEqual(
            new[]
            {
                ScpiMessageProducer.DisableStorageSd.Data,
                ScpiMessageProducer.EnableNetworkLan.Data,
                ScpiMessageProducer.SetStreamInterface(Daqifi.Core.Communication.StreamInterface.Usb).Data
            },
            device.SentCommands);
    }

    [TestMethod]
    public void HandleInboundMessage_WhenInLogToDevice_IgnoresStreamingSamples()
    {
        var device = new TestStreamingDevice();
        var coreChannel = new Daqifi.Core.Channel.AnalogChannel(0, 4096)
        {
            Name = "AI0",
            Direction = Daqifi.Core.Channel.ChannelDirection.Input,
            CalibrationB = 0,
            CalibrationM = 1,
            InternalScaleM = 1,
            PortRange = 5
        };

        var channel = new AnalogChannel(device, coreChannel)
        {
            IsActive = true
        };

        device.DataChannels.Add(channel);
        device.InitializeDeviceState();
        device.SwitchMode(DeviceMode.LogToDevice);
        device.IsStreaming = true;

        device.RouteInboundMessage(new DaqifiOutMessage
        {
            MsgTimeStamp = 1000,
            DeviceSn = 12345,
            DeviceFwRev = "1.0.0",
            AnalogInDataFloat = { 1.25f }
        });

        Assert.IsNull(channel.ActiveSample, "Streaming data should be ignored while the device is in LogToDevice mode.");
    }

    private static DaqifiDevice BuildCoreDeviceSnapshot(string firmwareVersion, float calibrationM)
    {
        var statusMessage = BuildStatusMessage(firmwareVersion, calibrationM);
        var coreDevice = new DaqifiDevice("Core Test Device");
        coreDevice.Metadata.UpdateFromProtobuf(statusMessage);
        coreDevice.PopulateChannelsFromStatus(statusMessage);
        return coreDevice;
    }

    private static Daqifi.Core.Channel.AnalogChannel BuildAnalogInputCoreChannel(int index)
    {
        return new Daqifi.Core.Channel.AnalogChannel(index, 4096)
        {
            Name = $"AI{index}",
            Direction = Daqifi.Core.Channel.ChannelDirection.Input,
            CalibrationB = 0,
            CalibrationM = 1,
            InternalScaleM = 1,
            PortRange = 5
        };
    }

    private static Daqifi.Core.Channel.DigitalChannel BuildDigitalInputCoreChannel(int index)
    {
        return new Daqifi.Core.Channel.DigitalChannel(index)
        {
            Name = $"DIO{index}",
            Direction = Daqifi.Core.Channel.ChannelDirection.Input
        };
    }

    private static DaqifiOutMessage BuildStatusMessage(string firmwareVersion, float calibrationM)
    {
        return new DaqifiOutMessage
        {
            DevicePn = "Nq1",
            DeviceSn = 12345,
            DeviceFwRev = firmwareVersion,
            AnalogInPortNum = 1,
            AnalogInRes = 4095,
            DigitalPortNum = 1,
            WifiSecurityMode = 0,
            WifiInfMode = (uint)WifiMode.ExistingNetwork,
            Ssid = "TestNetwork",
            AnalogInCalM = { calibrationM },
            AnalogInCalB = { 0.25f },
            AnalogInIntScaleM = { 1.0f },
            AnalogInPortRange = { 10.0f }
        };
    }

    /// <summary>
    /// Test implementation of AbstractStreamingDevice for testing purposes
    /// </summary>
    private class TestStreamingDevice : AbstractStreamingDevice
    {
        public List<string> SentCommands { get; } = [];

        public override ConnectionType ConnectionType => ConnectionType.Usb;

        public override bool Connect() => true;

        public override bool Disconnect() => true;

        public override bool Write(string command) => true;

        protected override void SendMessage(IOutboundMessage<string> message)
        {
            SentCommands.Add(message.Data);
        }

        public void RouteInboundMessage(DaqifiOutMessage message)
        {
            HandleInboundMessage(
                new MessageEventArgs<object>(
                    new GenericInboundMessage<object>(message)));
        }
    }

    private sealed class NetworkConfigurationTestDevice : AbstractStreamingDevice
    {
        private readonly RecordingCoreStreamingDevice _coreDevice;
        private readonly ConnectionType _connectionType;

        public NetworkConfigurationTestDevice(string? throwOnCommandData = null, ConnectionType connectionType = ConnectionType.Usb)
        {
            _coreDevice = new RecordingCoreStreamingDevice(SentCommands, throwOnCommandData);
            _coreDevice.Connect();
            _connectionType = connectionType;
        }

        public List<string> SentCommands { get; } = [];

        public override ConnectionType ConnectionType => _connectionType;

        protected override CoreStreamingDevice? CoreDeviceForNetworkConfiguration => _coreDevice;

        protected override CoreStreamingDevice? CoreDeviceForStreaming => _coreDevice;

        public override bool Connect() => true;

        public override bool Disconnect() => true;

        public override bool Write(string command) => true;

        protected override void SendMessage(IOutboundMessage<string> message)
        {
            SentCommands.Add($"desktop:{message.Data}");
        }
    }

    private sealed class CoreSynchronizationTestDevice : AbstractStreamingDevice
    {
        public override ConnectionType ConnectionType => ConnectionType.Usb;

        public override bool Connect() => true;

        public override bool Disconnect() => true;

        public override bool Write(string command) => true;

        public void ApplyCoreSnapshot(DaqifiDevice coreDevice)
        {
            SyncFromCoreDevice(coreDevice);
        }

        /// <summary>
        /// Simulates a <see cref="DaqifiDevice.ChannelsPopulated"/> event from a Core device.
        /// </summary>
        public void SimulateChannelsPopulated(DaqifiDevice coreDevice)
        {
            var args = new ChannelsPopulatedEventArgs(
                coreDevice.Channels,
                coreDevice.Channels.Count(c => c.Type == Daqifi.Core.Channel.ChannelType.Analog),
                coreDevice.Channels.Count(c => c.Type == Daqifi.Core.Channel.ChannelType.Digital));
            OnCoreChannelsPopulated(coreDevice, args);
        }

        /// <summary>
        /// Simulates a <see cref="DaqifiDevice.ChannelsPopulated"/> event with an arbitrary sender.
        /// </summary>
        public void SimulateChannelsPopulatedFromSender(object? sender, ChannelsPopulatedEventArgs args)
        {
            OnCoreChannelsPopulated(sender, args);
        }

        protected override void SendMessage(IOutboundMessage<string> message)
        {
        }
    }

    private sealed class SdCardLoggingTestDevice : AbstractStreamingDevice
    {
        private readonly RecordingCoreStreamingDevice _coreDevice;

        public SdCardLoggingTestDevice()
        {
            _coreDevice = new RecordingCoreStreamingDevice(SentCommands, throwOnCommandData: null);
            _coreDevice.Connect();
        }

        public List<string> SentCommands { get; } = [];

        public override ConnectionType ConnectionType => ConnectionType.Usb;

        protected override CoreStreamingDevice? CoreDeviceForSd => _coreDevice;

        public override bool Connect() => true;

        public override bool Disconnect() => true;

        public override bool Write(string command) => true;

        protected override void SendMessage(IOutboundMessage<string> message)
        {
            SentCommands.Add($"desktop:{message.Data}");
        }
    }

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
        }
    }

    private sealed class RecordingCoreStreamingDevice(List<string> sentCommands, string? throwOnCommandData) : CoreStreamingDevice("TestDevice")
    {
        public override void Send<T>(IOutboundMessage<T> message)
        {
            if (message is IOutboundMessage<string> stringMessage)
            {
                sentCommands.Add($"core:{stringMessage.Data}");

                if (throwOnCommandData == stringMessage.Data)
                {
                    throw new InvalidOperationException("Injected test failure.");
                }
            }
        }
    }
}
