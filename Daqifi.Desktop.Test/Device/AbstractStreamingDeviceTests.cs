using Daqifi.Desktop.Device;
using Daqifi.Desktop.Channel;
using Daqifi.Core.Device.Network;
using Daqifi.Core.Communication.Messages;
using Daqifi.Core.Communication.Producers;
using Daqifi.Core.Device; // Added for DeviceType from Core
using System.Windows.Media;
using CoreStreamingDevice = Daqifi.Core.Device.DaqifiStreamingDevice;

namespace Daqifi.Desktop.Test.Device;

[TestClass]
public class AbstractStreamingDeviceTests
{
    // First of the two commands Core composes for a friendly-name change; also the injection point
    // for the mid-sequence failure tests below.
    private const string SET_FRIENDLY_NAME_COMMAND = "SYSTem:DEVice:NAME \"Bench Rig 3\"";

    // "core:" prefixed because Core composes and sends these now (daqifi-core#302); the desktop
    // wrapper no longer builds the SCPI text itself. See RecordingCoreStreamingDevice.
    private static readonly string[] ExpectedFriendlyNameCommands =
        [$"core:{SET_FRIENDLY_NAME_COMMAND}", "core:SYSTem:DEVice:NAME:SAVE"];

    [TestMethod]
    public void FriendlyName_Property_ShouldDefaultToEmpty()
    {
        // Arrange & Act
        var device = new TestStreamingDevice();

        // Assert
        Assert.AreEqual(string.Empty, device.FriendlyName);
    }

    [TestMethod]
    public void StatusMessageWithFriendlyDeviceName_UpdatesFriendlyName()
    {
        // Arrange
        var device = new TestStreamingDevice();

        // Act — firmware's fast streaming-frame encoder (Nanopb_EncodeStreamingFast) hardcodes
        // only msg_time_stamp/analog_in_data/digital_data/digital_port_dir; friendly_device_name
        // is only ever populated on "info" responses like SYSTem:SYSInfoPB? (Core sends this once
        // during InitializeAsync). Core classifies a message with no analog/digital sample data but
        // a nonzero port-count field as Status and raises StatusMessageReceived for it.
        device.RouteStatusMessage(new DaqifiOutMessage
        {
            AnalogInPortNum = 1,
            DeviceSn = 12345,
            DeviceFwRev = "3.7.2",
            FriendlyDeviceName = "Bench Rig 3"
        });

        // Assert
        Assert.AreEqual("Bench Rig 3", device.FriendlyName);
    }

    [TestMethod]
    public void StreamMessageWithFriendlyDeviceName_UpdatesFriendlyName()
    {
        // Arrange — belt-and-suspenders: a real Stream-classified frame never carries this field
        // (see the Status-message test above), but the desktop code captures it there too in
        // case firmware's streaming field set ever changes.
        var device = new TestStreamingDevice();

        // Act
        device.RouteStreamMessage(new DaqifiOutMessage
        {
            MsgTimeStamp = 1000,
            DeviceSn = 12345,
            FriendlyDeviceName = "Bench Rig 3",
            AnalogInDataFloat = { 1.25f }
        });

        // Assert
        Assert.AreEqual("Bench Rig 3", device.FriendlyName);
    }

    [TestMethod]
    public void StatusMessageWithoutFriendlyDeviceName_ClearsStaleFriendlyName()
    {
        // Arrange — e.g. SerialStreamingDevice instances are reused across reconnects on the same
        // COM port (ConnectionDialogViewModel dedups discovery by port), so a name captured from a
        // previously connected device must not leak onto a different/renamed device that reports
        // no name (issue #83 Qodo review: "stale friendlyname leaks").
        //
        // This is also why FriendlyName is not folded into Core's Metadata.FriendlyName: Core's
        // UpdateFromProtobuf assigns the name only when non-empty, so it can never clear.
        var device = new TestStreamingDevice();
        device.RouteStatusMessage(new DaqifiOutMessage
        {
            AnalogInPortNum = 1,
            DeviceSn = 12345,
            FriendlyDeviceName = "Bench Rig 3"
        });
        Assert.AreEqual("Bench Rig 3", device.FriendlyName, "Precondition: name captured from the first status message.");

        // Act — a fresh connect's status response with no name (firmware always includes the
        // field, empty when unset) is authoritative and must clear the stale value.
        device.RouteStatusMessage(new DaqifiOutMessage
        {
            AnalogInPortNum = 1,
            DeviceSn = 67890
        });

        // Assert
        Assert.AreEqual(string.Empty, device.FriendlyName);
    }

    [TestMethod]
    public void StreamMessageWithoutFriendlyDeviceName_LeavesFriendlyNameUnchanged()
    {
        // Arrange — a Stream-classified frame never carries this field at all (firmware's fast
        // streaming encoder omits it), so it must not clobber the value the status response
        // already captured — unlike the status-message case above, which is authoritative.
        var device = new TestStreamingDevice();
        device.RouteStatusMessage(new DaqifiOutMessage
        {
            AnalogInPortNum = 1,
            DeviceSn = 12345,
            FriendlyDeviceName = "Bench Rig 3"
        });

        // Act
        device.RouteStreamMessage(new DaqifiOutMessage
        {
            MsgTimeStamp = 1000,
            DeviceSn = 12345,
            AnalogInDataFloat = { 1.30f }
        });

        // Assert
        Assert.AreEqual("Bench Rig 3", device.FriendlyName);
    }

    [TestMethod]
    public void DeviceDisplayName_PrefersFriendlyNameOverSerialNumber()
    {
        // Arrange
        var device = new TestStreamingDevice
        {
            DeviceSerialNo = "12345"
        };
        device.RouteStatusMessage(new DaqifiOutMessage
        {
            AnalogInPortNum = 1,
            DeviceSn = 12345,
            FriendlyDeviceName = "Bench Rig 3"
        });

        // Act & Assert
        Assert.AreEqual("Bench Rig 3", device.DeviceDisplayName);
    }

    [TestMethod]
    public void DeviceDisplayName_FallsBackToSerialNumber_WhenNoFriendlyName()
    {
        // Arrange
        var device = new TestStreamingDevice
        {
            DeviceSerialNo = "12345"
        };

        // Act & Assert
        Assert.AreEqual("12345", device.DeviceDisplayName);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("this name is way too long to fit in the 31-char firmware NVM buffer")]
    [DataRow("Has a \"quote\"")]
    [DataRow("Has a \\backslash")]
    [DataRow("Has a \u0007bell")]
    public void SetFriendlyName_WithInvalidName_ThrowsArgumentException(string invalidName)
    {
        // Arrange
        var device = new TestStreamingDevice();
        device.SetCoreDeviceConnected(true);

        // Act & Assert
        Assert.ThrowsExactly<ArgumentException>(() => device.SetFriendlyName(invalidName));
        Assert.AreEqual(0, device.SentCommands.Count, "No SCPI command should be sent for an invalid name.");
    }

    [TestMethod]
    public void SetFriendlyName_WithValidName_SendsSetThenSaveAndUpdatesProperty()
    {
        // Arrange
        var device = new TestStreamingDevice();
        device.SetCoreDeviceConnected(true);

        // Act
        device.SetFriendlyName("Bench Rig 3");

        // Assert
        CollectionAssert.AreEqual(
            ExpectedFriendlyNameCommands,
            device.SentCommands);
        Assert.AreEqual("Bench Rig 3", device.FriendlyName, "FriendlyName should update optimistically.");
    }

    [TestMethod]
    public void SetFriendlyName_AtMaxLength_Succeeds()
    {
        // Arrange — length taken from Core so this cannot drift from the bound it is testing.
        var device = new TestStreamingDevice();
        device.SetCoreDeviceConnected(true);
        var maxLengthName = new string('A', ScpiMessageProducer.MaxFriendlyNameLength);

        // Act
        device.SetFriendlyName(maxLengthName);

        // Assert
        Assert.AreEqual(maxLengthName, device.FriendlyName);
    }

    [TestMethod]
    public void SetFriendlyName_OneOverMaxLength_ThrowsArgumentException()
    {
        // Arrange
        var device = new TestStreamingDevice();
        device.SetCoreDeviceConnected(true);
        var tooLongName = new string('A', ScpiMessageProducer.MaxFriendlyNameLength + 1);

        // Act & Assert
        Assert.ThrowsExactly<ArgumentException>(() => device.SetFriendlyName(tooLongName));
        Assert.AreEqual(0, device.SentCommands.Count);
    }

    /// <summary>
    /// Validation must run before the connected check, not after. Core's
    /// <c>SetFriendlyNameAsync</c> validates only after its own connected check and throws when
    /// disconnected, so leaning on Core's validation alone would let an invalid name pass silently
    /// whenever no device is attached — the exact case the Devices drawer hits while the user types
    /// with nothing connected.
    /// </summary>
    [TestMethod]
    public void SetFriendlyName_WithInvalidNameWhileDisconnected_StillThrows()
    {
        // Arrange
        var device = new TestStreamingDevice();
        device.SetCoreDeviceConnected(false);

        // Act & Assert
        Assert.ThrowsExactly<ArgumentException>(() => device.SetFriendlyName("Has a \"quote\""));
    }

    [TestMethod]
    public void SetFriendlyName_WhenDisconnected_NoOpsWithoutThrowing()
    {
        // Arrange
        var device = new TestStreamingDevice();
        device.SetCoreDeviceConnected(false);

        // Act
        device.SetFriendlyName("Bench Rig 3");

        // Assert
        Assert.AreEqual(0, device.SentCommands.Count);
        Assert.AreEqual(string.Empty, device.FriendlyName, "A disconnected no-op must not update the local property.");
    }

    /// <summary>
    /// The device can drop after the connected check passes — <c>CleanupConnection</c> clears
    /// <c>CoreDevice</c> from whichever thread notices the disconnect — and Core's own guards throw
    /// where the pre-delegation send path logged and returned. That no-op has to survive delegation:
    /// <c>DaqifiViewModel.SetFriendlyName</c> treats <see cref="ArgumentException"/> as the only
    /// expected failure, so a disconnect race must not escape the UI command as a different type.
    /// </summary>
    [TestMethod]
    public void SetFriendlyName_WhenDeviceDisconnectsMidCall_NoOpsWithoutThrowing()
    {
        // Arrange
        var device = new TestStreamingDevice();
        device.SetCoreDeviceFailingOn(SET_FRIENDLY_NAME_COMMAND, disconnectOnThrow: true);

        // Act
        device.SetFriendlyName("Bench Rig 3");

        // Assert
        Assert.AreEqual(
            string.Empty, device.FriendlyName, "A send that never reached the device must not update the property.");
    }

    [TestMethod]
    public void SetFriendlyName_WhenCoreFailsWhileStillConnected_Propagates()
    {
        // Arrange
        var device = new TestStreamingDevice();
        device.SetCoreDeviceFailingOn(SET_FRIENDLY_NAME_COMMAND, disconnectOnThrow: false);

        // Act & Assert — a failure that is not a disconnect is a real fault. Swallowing it here
        // would report success to the user and hide the cause (the masking trap from issue #619).
        Assert.ThrowsExactly<InvalidOperationException>(() => device.SetFriendlyName("Bench Rig 3"));
        Assert.AreEqual(string.Empty, device.FriendlyName);
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
        using var device = new NetworkConfigurationTestDevice();
        device.NetworkConfiguration = new NetworkConfiguration(
            WifiMode.ExistingNetwork,
            WifiSecurityType.WpaPskPhrase,
            "TestNetwork",
            "TestPassword");
        device.IsStreaming = true;
        device.SetCoreStreaming();

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
        using var device = new NetworkConfigurationTestDevice();
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
        using var device = new NetworkConfigurationTestDevice(connectionType: ConnectionType.Wifi);
        device.NetworkConfiguration = new NetworkConfiguration(
            WifiMode.ExistingNetwork,
            WifiSecurityType.WpaPskPhrase,
            "TestNetwork",
            "TestPassword");

        // Act
        await device.UpdateNetworkConfiguration();

        // Assert — the SD-restore path (PrepareSdInterface) must not run for a WiFi device.
        // EnableStorageSd is emitted only by PrepareSdInterface, never by Core's network update,
        // so its absence in either layer proves the SD interface was not restored.
        Assert.IsFalse(
            device.SentCommands.Contains($"desktop:{ScpiMessageProducer.EnableStorageSd.Data}"),
            "Desktop should not re-enable SD for a WiFi device; it shares no SPI bus with the desktop transport.");
        Assert.IsFalse(
            device.SentCommands.Contains($"core:{ScpiMessageProducer.EnableStorageSd.Data}"),
            "Core's SD-enable command must not run for a WiFi device; the SD interface is never restored.");
    }

    [TestMethod]
    public async Task UpdateNetworkConfiguration_WhenInLogToDevice_RestoresSdInterfaceAfterCoreUpdate()
    {
        // Arrange
        using var device = new NetworkConfigurationTestDevice();
        device.NetworkConfiguration = new NetworkConfiguration(
            WifiMode.SelfHosted,
            WifiSecurityType.None,
            "DAQiFi_Device",
            string.Empty);
        device.SwitchMode(DeviceMode.LogToDevice);
        device.SentCommands.Clear();

        // Act
        await device.UpdateNetworkConfiguration();

        // Assert — Core 1.3.0 persists before applying (daqifi-core#352), so SAVE precedes APPLY.
        CollectionAssert.AreEqual(
            new[]
            {
                $"core:{ScpiMessageProducer.SaveNetworkLan.Data}",
                $"core:{ScpiMessageProducer.ApplyNetworkLan.Data}",
                $"core:{ScpiMessageProducer.DisableNetworkLan.Data}",
                $"core:{ScpiMessageProducer.EnableStorageSd.Data}",
                $"desktop:{ScpiMessageProducer.SetStreamInterface(Daqifi.Core.Communication.StreamInterface.SdCard).Data}"
            },
            device.SentCommands.TakeLast(5).ToArray(),
            "Core should own the SD/LAN interface SCPI pair; the desktop only adds the USB " +
            "stream-interface switch when restoring the SD interface in LogToDevice mode.");
    }

    [TestMethod]
    public async Task UpdateNetworkConfiguration_WhenCoreUpdateThrowsInLogToDevice_RestoresSdInterface()
    {
        // Arrange
        using var device = new NetworkConfigurationTestDevice(throwOnCommandData: ScpiMessageProducer.SaveNetworkLan.Data);
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
                $"core:{ScpiMessageProducer.DisableNetworkLan.Data}",
                $"core:{ScpiMessageProducer.EnableStorageSd.Data}",
                $"desktop:{ScpiMessageProducer.SetStreamInterface(Daqifi.Core.Communication.StreamInterface.SdCard).Data}"
            },
            device.SentCommands.TakeLast(3).ToArray(),
            "Desktop should restore the full SD interface even when the Core network update fails " +
            "(Core owns the LAN-disable/SD-enable pair).");
    }

    [TestMethod]
    public async Task UpdateNetworkConfiguration_WhenSdRestoreFailsAfterDisconnect_PreservesOriginalException()
    {
        // Arrange — USB + LogToDevice so the finally restores the SD interface. The injected Core
        // update failure also drops the Core device (as a mid-update disconnect would, via
        // CleanupConnection nulling CoreDevice), so the finally's PrepareSdInterface would throw
        // "Device is not connected." if the restore were not best-effort.
        using var device = new NetworkConfigurationTestDevice(
            throwOnCommandData: ScpiMessageProducer.SaveNetworkLan.Data,
            dropCoreDeviceOnThrow: true);
        device.NetworkConfiguration = new NetworkConfiguration(
            WifiMode.SelfHosted,
            WifiSecurityType.None,
            "DAQiFi_Device",
            string.Empty);
        device.SwitchMode(DeviceMode.LogToDevice);

        // Act + Assert — the original Core update failure must surface, not the restore failure.
        try
        {
            await device.UpdateNetworkConfiguration();
            Assert.Fail("Expected the Core update failure to propagate.");
        }
        catch (InvalidOperationException exception)
        {
            Assert.AreEqual(
                "Injected test failure.",
                exception.Message,
                "The best-effort SD restore must not mask the original network update failure.");
        }
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
        // Set direction/output on the core channel directly (the desktop Direction setter
        // would also issue a device command through the owner).
        digitalChannel.CoreChannel.Direction = Daqifi.Core.Channel.ChannelDirection.Output;
        digitalChannel.CoreChannel.OutputValue = true;

        // A refresh re-populates the same Core device — Core updates its channels in place
        // (daqifi-core#309) rather than building new instances.
        RepopulateCoreDevice(initialCoreDevice, firmwareVersion: "2.0.0", calibrationM: 2.5f);

        // Act
        device.ApplyCoreSnapshot(initialCoreDevice);

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
        Assert.AreEqual(
            Daqifi.Core.Channel.ChannelDirection.Output,
            refreshedDigitalChannel.Direction,
            "Channel direction should survive a channel refresh.");
        Assert.IsTrue(
            refreshedDigitalChannel.CoreChannel.OutputValue,
            "Commanded output state should survive a channel refresh (issue #663).");
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

        // Act — same-session channel refresh (e.g., re-query device info) on the same Core device,
        // which is the only shape a refresh takes in production
        RepopulateCoreDevice(coreDevice, firmwareVersion: "1.0.0", calibrationM: 2.0f);
        device.SimulateChannelsPopulated(coreDevice);

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
    [DataRow(ConnectionStatus.Lost)]
    [DataRow(ConnectionStatus.Failed)]
    [DataRow(ConnectionStatus.Disconnected)]
    public void OnCoreStatusChanged_UnexpectedDrop_NotifiesIsConnectedAndRaisesConnectionLost(ConnectionStatus status)
    {
        // Arrange — issue #638: the desktop never subscribed to Core's StatusChanged at all, so
        // IsConnected never raised a change notification when Core detected a spontaneous drop.
        var device = new CoreSynchronizationTestDevice();
        var coreDevice = BuildCoreDeviceSnapshot(firmwareVersion: "1.0.0", calibrationM: 1.0f);
        device.SimulateChannelsPopulated(coreDevice);

        var raisedPropertyNames = new List<string?>();
        device.PropertyChanged += (_, e) => raisedPropertyNames.Add(e.PropertyName);

        ConnectionLostEventArgs? capturedArgs = null;
        device.ConnectionLost += (_, e) => capturedArgs = e;

        // Act
        device.SimulateStatusChanged(coreDevice, status);

        // Assert
        Assert.IsTrue(
            raisedPropertyNames.Contains(nameof(Daqifi.Desktop.Device.IStreamingDevice.IsConnected)),
            "IsConnected must raise a PropertyChanged notification so bound UI " +
            "(e.g. the device tile) refreshes.");
        Assert.AreEqual(DeviceState.Disconnected, device.DeviceState);
        Assert.IsNotNull(
            capturedArgs,
            "ConnectionLost should fire so ConnectionManager can tear down and notify the user.");
    }

    [TestMethod]
    [DataRow(ConnectionStatus.Connecting)]
    [DataRow(ConnectionStatus.Connected)]
    [DataRow(ConnectionStatus.Retrying)]
    public void OnCoreStatusChanged_NonDropStatus_DoesNotRaiseConnectionLost(ConnectionStatus status)
    {
        // Arrange
        var device = new CoreSynchronizationTestDevice();
        var coreDevice = BuildCoreDeviceSnapshot(firmwareVersion: "1.0.0", calibrationM: 1.0f);
        device.SimulateChannelsPopulated(coreDevice);

        var connectionLostRaised = false;
        device.ConnectionLost += (_, _) => connectionLostRaised = true;

        // Act
        device.SimulateStatusChanged(coreDevice, status);

        // Assert
        Assert.IsFalse(
            connectionLostRaised,
            $"{status} is not an unexpected drop and must not raise ConnectionLost.");
    }

    [TestMethod]
    public void StartSdCardLogging_WhenSynchronizationContextIsBlocked_CompletesWithoutDeadlock()
    {
        // Arrange
        using var device = new SdCardLoggingTestDevice();
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
    public void StartSdCardLogging_NoLongerHandRollsChannelConfiguration()
    {
        // Core's StartSdCardLoggingAsync(channelMask: null) now computes the ADC enable mask and
        // DIO enable state itself from its own channel configuration (issue #706) — the desktop
        // no longer builds an analog mask or sends EnableDioPorts/DisableDioPorts directly. The
        // mask/DIO computation itself is Core's responsibility and is covered by Core's own tests.
        using var device = new SdCardLoggingTestDevice();
        device.SwitchMode(DeviceMode.LogToDevice);

        device.StartSdCardLogging();

        Assert.IsFalse(
            device.SentCommands.Any(c => c.StartsWith("desktop:", StringComparison.Ordinal)),
            "The desktop should not send any SCPI commands directly for SD logging start — " +
            "channel configuration now flows entirely through Core's channelMask: null path.");
    }

    [TestMethod]
    public void GetSdCardParseConfiguration_WithAnalogChannels_ReturnsCoreConfiguration()
    {
        using var device = new SdCardLoggingTestDevice();
        device.PopulateCoreChannels(BuildStatusMessage("1.0.0", 1.5f));

        var config = device.GetSdCardParseConfiguration();

        Assert.IsNotNull(config, "A device with analog channels should produce a configuration.");
        Assert.AreEqual(1, config.AnalogPortCount);
        Assert.AreEqual(1, config.DigitalPortCount);
        Assert.AreEqual(1.5d, config.CalibrationValues![0].Slope, 0.001d);
        Assert.AreEqual(0u, config.TimestampFrequency,
            "TimestampFrequency should be 0 so the parser falls back to file-embedded/device frequency.");
    }

    [TestMethod]
    public void GetSdCardParseConfiguration_WithoutCoreDevice_ReturnsNull()
    {
        var device = new TestStreamingDevice();

        Assert.IsNull(device.GetSdCardParseConfiguration(),
            "A device with no Core device for SD operations should return null.");
    }

    [TestMethod]
    public void GetSdCardParseConfiguration_WhenNotUsbConnected_ReturnsNull()
    {
        // A Core SD device could theoretically still be set on a non-USB connection type;
        // the USB gate must be explicit rather than relying on that being impossible.
        using var device = new NonUsbSdCoreTestDevice();
        device.PopulateCoreChannels(BuildStatusMessage("1.0.0", 1.5f));

        Assert.IsNull(device.GetSdCardParseConfiguration(),
            "SD parse configuration should only be available over a USB connection.");
    }

    [TestMethod]
    public void SwitchMode_WhenEnteringLogToDevice_DoesNotSendInterfaceCommands()
    {
        // Core's StartSdCardLoggingAsync now handles SD interface setup,
        // so SwitchMode should not eagerly send these commands.
        var device = new TestStreamingDevice();

        device.SwitchMode(DeviceMode.LogToDevice);

        Assert.AreEqual(0, device.SentCommands.Count,
            "No commands should be sent when switching to LogToDevice — " +
            "Core handles SD interface setup at logging start.");
    }

    [TestMethod]
    public void SwitchMode_WhenReturningToStreamToApp_SetsUsbStreamInterface()
    {
        // PrepareLanInterface now delegates the SD/LAN pair to the connected Core device,
        // so this scenario needs a Core-backed harness rather than the bare TestStreamingDevice.
        using var device = new NetworkConfigurationTestDevice();
        device.SwitchMode(DeviceMode.LogToDevice);
        device.SentCommands.Clear();

        device.SwitchMode(DeviceMode.StreamToApp);

        CollectionAssert.AreEqual(
            new[]
            {
                $"core:{ScpiMessageProducer.DisableStorageSd.Data}",
                $"core:{ScpiMessageProducer.EnableNetworkLan.Data}",
                $"desktop:{ScpiMessageProducer.SetStreamInterface(Daqifi.Core.Communication.StreamInterface.Usb).Data}"
            },
            device.SentCommands,
            "Core should own the SD-disable/LAN-enable pair when returning to StreamToApp; " +
            "the desktop only adds the USB stream-interface switch.");
    }

    [TestMethod]
    public void StreamMessage_WhenInLogToDevice_IgnoresStreamingSamples()
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
        device.SwitchMode(DeviceMode.LogToDevice);
        device.IsStreaming = true;

        device.RouteStreamMessage(new DaqifiOutMessage
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
        var coreDevice = new DaqifiDevice("Core Test Device");
        RepopulateCoreDevice(coreDevice, firmwareVersion, calibrationM);
        return coreDevice;
    }

    /// <summary>
    /// Re-runs metadata + channel population on an existing Core device, which is what a routine
    /// status frame does in production. A same-session refresh always comes from the one Core
    /// device the wrapper connected — a reconnect builds a new Core device but also clears
    /// <c>DataChannels</c> first, so a wrapper never survives to meet a different Core instance.
    /// </summary>
    private static void RepopulateCoreDevice(DaqifiDevice coreDevice, string firmwareVersion, float calibrationM)
    {
        var statusMessage = BuildStatusMessage(firmwareVersion, calibrationM);
        coreDevice.Metadata.UpdateFromProtobuf(statusMessage);
        coreDevice.PopulateChannelsFromStatus(statusMessage);
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
    private sealed class TestStreamingDevice : AbstractStreamingDevice
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

        /// <summary>
        /// Invokes the handler Core calls when it classifies a frame as a status message and raises
        /// <c>StatusMessageReceived</c> (daqifi-core#308). The desktop no longer classifies frames
        /// itself, so which handler a given frame reaches is Core's decision and these tests state
        /// it explicitly rather than re-deriving it.
        /// </summary>
        public void RouteStatusMessage(DaqifiOutMessage message) => OnStatusMessageReceived(message);

        /// <summary>
        /// Invokes the handler Core calls when it classifies a frame as a streaming data message and
        /// raises <c>StreamMessageReceived</c>. Core's own decode step is not driven here.
        /// </summary>
        public void RouteStreamMessage(DaqifiOutMessage message) => OnStreamMessageReceived(message);

        /// <summary>
        /// Fakes a connected Core device (transport-less) so
        /// <see cref="IStreamingDevice.IsConnected"/>-gated commands can be exercised without a
        /// real connect flow. Pass <c>false</c> to simulate a disconnected/missing Core device.
        /// </summary>
        /// <remarks>
        /// Uses <see cref="RecordingCoreStreamingDevice"/> so commands Core composes on the
        /// desktop's behalf land in <see cref="SentCommands"/> (prefixed <c>core:</c>) alongside
        /// the ones the wrapper sends itself (unprefixed, via the <c>SendMessage</c> override).
        /// Friendly-name commands moved to the Core side in daqifi-core#302.
        /// </remarks>
        public void SetCoreDeviceConnected(bool connected)
        {
            if (!connected)
            {
                CoreDevice = null;
                return;
            }

            var coreDevice = new RecordingCoreStreamingDevice(SentCommands, throwOnCommandData: null);
            coreDevice.Connect();
            CoreDevice = coreDevice;
        }

        /// <summary>
        /// Fakes a connected Core device whose send of <paramref name="commandData"/> throws.
        /// With <paramref name="disconnectOnThrow"/> the Core device drops its connection first,
        /// reproducing a disconnect that lands between the wrapper's connected check and Core's own
        /// guard; without it the failure stands for an unrelated Core fault, which must propagate.
        /// </summary>
        public void SetCoreDeviceFailingOn(string commandData, bool disconnectOnThrow)
        {
            RecordingCoreStreamingDevice? coreDevice = null;
            coreDevice = new RecordingCoreStreamingDevice(
                SentCommands,
                commandData,
                onThrow: disconnectOnThrow ? () => coreDevice!.Disconnect() : null);
            coreDevice.Connect();
            CoreDevice = coreDevice;
        }
    }

    private sealed class NetworkConfigurationTestDevice : AbstractStreamingDevice, IDisposable
    {
        private readonly RecordingCoreStreamingDevice _coreDevice;
        private readonly ConnectionType _connectionType;

        // The Core device connected in the constructor is owned by this fixture; disposing it
        // keeps the suite from leaking one connected device per test (CA1001).
        public void Dispose() => _coreDevice.Dispose();
        private bool _coreDeviceAvailable = true;

        public NetworkConfigurationTestDevice(
            string? throwOnCommandData = null,
            ConnectionType connectionType = ConnectionType.Usb,
            bool dropCoreDeviceOnThrow = false)
        {
            // When dropCoreDeviceOnThrow is set, the injected failure also drops the Core device,
            // mirroring a mid-update disconnect where CleanupConnection nulls CoreDevice. The
            // finally-block SD restore then sees a null Core device.
            _coreDevice = new RecordingCoreStreamingDevice(
                SentCommands,
                throwOnCommandData,
                onThrow: dropCoreDeviceOnThrow ? () => _coreDeviceAvailable = false : null);
            _coreDevice.Connect();
            _connectionType = connectionType;
        }

        public List<string> SentCommands { get; } = [];

        public override ConnectionType ConnectionType => _connectionType;

        protected override CoreStreamingDevice? CoreDeviceForNetworkConfiguration =>
            _coreDeviceAvailable ? _coreDevice : null;

        protected override CoreStreamingDevice? CoreDeviceForStreaming => _coreDevice;

        /// <summary>
        /// Puts the Core device into the streaming state and clears recorded commands,
        /// so that subsequent stop-streaming calls flow through the Core layer correctly.
        /// </summary>
        public void SetCoreStreaming()
        {
            _coreDevice.StreamingFrequency = 1;
            _coreDevice.StartStreaming();
            SentCommands.Clear();
        }

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

        /// <summary>
        /// Simulates Core's <see cref="Daqifi.Core.Device.IDevice.StatusChanged"/> event
        /// (issue #638) without a real transport.
        /// </summary>
        public void SimulateStatusChanged(DaqifiDevice coreDevice, ConnectionStatus status)
        {
            OnCoreStatusChanged(coreDevice, new DeviceStatusEventArgs(status));
        }

        protected override void SendMessage(IOutboundMessage<string> message)
        {
        }
    }

    private sealed class SdCardLoggingTestDevice : AbstractStreamingDevice, IDisposable
    {
        private readonly RecordingCoreStreamingDevice _coreDevice;

        // The Core device connected in the constructor is owned by this fixture; disposing it
        // keeps the suite from leaking one connected device per test (CA1001).
        public void Dispose() => _coreDevice.Dispose();

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

        public void PopulateCoreChannels(DaqifiOutMessage message)
        {
            _coreDevice.PopulateChannelsFromStatus(message);
        }

        protected override void SendMessage(IOutboundMessage<string> message)
        {
            SentCommands.Add($"desktop:{message.Data}");
        }
    }

    /// <summary>
    /// A non-USB-connected device that still has a Core device wired up for SD operations,
    /// so tests can prove <see cref="AbstractStreamingDevice.GetSdCardParseConfiguration"/>
    /// gates on <see cref="ConnectionType"/> rather than only on <c>CoreDeviceForSd</c>.
    /// </summary>
    private sealed class NonUsbSdCoreTestDevice : AbstractStreamingDevice, IDisposable
    {
        private readonly RecordingCoreStreamingDevice _coreDevice;

        // The Core device connected in the constructor is owned by this fixture; disposing it
        // keeps the suite from leaking one connected device per test (CA1001).
        public void Dispose() => _coreDevice.Dispose();

        public NonUsbSdCoreTestDevice()
        {
            _coreDevice = new RecordingCoreStreamingDevice([], throwOnCommandData: null);
            _coreDevice.Connect();
        }

        public override ConnectionType ConnectionType => ConnectionType.Wifi;

        protected override CoreStreamingDevice? CoreDeviceForSd => _coreDevice;

        public override bool Connect() => true;

        public override bool Disconnect() => true;

        public override bool Write(string command) => true;

        public void PopulateCoreChannels(DaqifiOutMessage message)
        {
            _coreDevice.PopulateChannelsFromStatus(message);
        }

        protected override void SendMessage(IOutboundMessage<string> message)
        {
        }
    }

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
        }
    }

    private sealed class RecordingCoreStreamingDevice(
        List<string> sentCommands,
        string? throwOnCommandData,
        Action? onThrow = null) : CoreStreamingDevice("TestDevice")
    {
        public override bool IsUsbConnection => true;

        public override void Send<T>(IOutboundMessage<T> message)
        {
            if (message is IOutboundMessage<string> stringMessage)
            {
                sentCommands.Add($"core:{stringMessage.Data}");

                if (throwOnCommandData == stringMessage.Data)
                {
                    onThrow?.Invoke();
                    throw new InvalidOperationException("Injected test failure.");
                }
            }
        }
    }
}
