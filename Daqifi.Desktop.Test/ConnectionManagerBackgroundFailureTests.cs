using System.Reflection;
using Daqifi.Core.Communication.Messages;
using Daqifi.Core.Communication.Producers;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
using Moq;
using CoreDeviceErrorEventArgs = Daqifi.Core.Device.DeviceErrorEventArgs;
using CoreSendFailedEventArgs = Daqifi.Core.Communication.Producers.MessageSendFailedEventArgs<string>;
using DeviceErrorSource = Daqifi.Core.Device.DeviceErrorSource;

namespace Daqifi.Desktop.Test;

/// <summary>
/// Tests for <see cref="ConnectionManager"/>'s routing of Core 1.4.0's background-failure events
/// (issue #805). Core made two previously invisible failure classes observable — faults on a
/// device's read/decode threads (<c>ErrorOccurred</c>) and fire-and-forget writes that never
/// reached the device (<c>SendFailed</c>) — and the desktop subscribed to neither.
/// <para>
/// Every case asserts the log level actually used, via an injected <see cref="IAppLogger"/>, and
/// checks BOTH <see cref="IAppLogger.Error(Exception, string)"/> and
/// <see cref="IAppLogger.Error(string)"/>, because both reach Sentry — the exception overload via
/// <c>CaptureException</c>, the message-only one via a synthesized <c>AppLogErrorException</c>.
/// Asserting "did not throw" would be true of every arm and is exactly how routing regressions
/// #775, #779 and #801 stayed green while environmental conditions were filed as app bugs.
/// </para>
/// <para>
/// Each test builds its own <see cref="ConnectionManager"/> through the internal test constructor
/// rather than touching <see cref="ConnectionManager.Instance"/>: the singleton is process-wide,
/// and MSTest parallelizes test classes, so a shared sink would collect other classes' logging.
/// </para>
/// </summary>
[TestClass]
public class ConnectionManagerBackgroundFailureTests
{
    private const string DISPLAY_NAME = "Nyquist-1 (SN-805)";

    // A password-carrying SCPI command is the reason send-failure logging reports the verb only.
    private const string WIFI_PASSWORD = "hunter2-not-in-the-log";

    #region ErrorOccurred severity mapping
    [TestMethod]
    [DataRow(DeviceErrorSource.MessageConsumer)]
    [DataRow(DeviceErrorSource.StreamDecode)]
    [DataRow(DeviceErrorSource.Reconnect)]
    public void ErrorOccurred_WithEnvironmentalSource_LogsWarningAndNotError(DeviceErrorSource source)
    {
        // Every source Core 1.4.0 actually raises describes the link or the device, not the app: a
        // read that failed because the cable came out, a frame the device garbled, a reconnect that
        // ran out of attempts against a powered-off unit. Routing these to Error would file a Sentry
        // event every time a user unplugs something and bury the real bugs (#775, #779, #801).
        var logger = new Mock<IAppLogger>();
        var device = CreateDevice();
        _ = CreateSubscribedManager(logger, device);
        var error = new IOException("The device is not connected.");

        device.Raise(d => d.ErrorOccurred += null, device.Object, new CoreDeviceErrorEventArgs(source, error));

        logger.Verify(l => l.Warning(error, It.IsAny<string>()), Times.Once);
        logger.Verify(l => l.Error(It.IsAny<Exception>(), It.IsAny<string>()), Times.Never);
        logger.Verify(l => l.Error(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public void ErrorOccurred_WithUnknownSource_LogsError()
    {
        // The guard in the other direction. No Core 1.4.0 path raises Unknown, so one arriving means
        // Core caught a failure it could not classify — expected volume zero, high signal. Same call
        // already made for SerialPortConnectFailure.Unknown in #801.
        var logger = new Mock<IAppLogger>();
        var device = CreateDevice();
        _ = CreateSubscribedManager(logger, device);
        var error = new InvalidOperationException("Something Core could not classify.");

        device.Raise(
            d => d.ErrorOccurred += null,
            device.Object,
            new CoreDeviceErrorEventArgs(DeviceErrorSource.Unknown, error));

        // Via the exception-carrying overload specifically: Error(string) would synthesize its own
        // exception and strand the real stack trace out of Sentry.
        logger.Verify(l => l.Error(error, It.IsAny<string>()), Times.Once);
        logger.Verify(l => l.Error(It.IsAny<string>()), Times.Never);
        logger.Verify(l => l.Warning(It.IsAny<Exception>(), It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public void ErrorOccurred_WithSourceThisBuildDoesNotRecognise_LogsWarningAndNotError()
    {
        // A source value the desktop has never heard of means this build is behind Core, not that
        // the device misbehaved — so it must not be blanket-escalated into Sentry when a Core bump
        // introduces a chatty new source.
        var logger = new Mock<IAppLogger>();
        var device = CreateDevice();
        _ = CreateSubscribedManager(logger, device);
        var error = new IOException("From a source added after this build.");

        device.Raise(
            d => d.ErrorOccurred += null,
            device.Object,
            new CoreDeviceErrorEventArgs((DeviceErrorSource)999, error));

        logger.Verify(l => l.Warning(error, It.IsAny<string>()), Times.Once);
        logger.Verify(l => l.Error(It.IsAny<Exception>(), It.IsAny<string>()), Times.Never);
        logger.Verify(l => l.Error(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public void IsAppBug_ClassifiesExactlyTheSourcesCoreDeclares()
    {
        // Tripwire for a Core bump: if daqifi-core adds or removes a DeviceErrorSource, this fails
        // by name rather than letting the new source silently inherit the default arm.
        var expected = new Dictionary<DeviceErrorSource, bool>
        {
            [DeviceErrorSource.Unknown] = true,
            [DeviceErrorSource.MessageConsumer] = false,
            [DeviceErrorSource.StreamDecode] = false,
            [DeviceErrorSource.Reconnect] = false
        };

        CollectionAssert.AreEquivalent(
            expected.Keys.ToList(),
            Enum.GetValues<DeviceErrorSource>().ToList(),
            "Core's DeviceErrorSource set changed; revisit the Warning/Error mapping before updating this list.");

        foreach (var (source, isAppBug) in expected)
        {
            Assert.AreEqual(isAppBug, ConnectionManager.IsAppBug(source), $"Unexpected classification for {source}.");
        }
    }

    [TestMethod]
    public void ErrorOccurred_ReportsCoresSuppressedCountRatherThanThrottlingAgain()
    {
        // Core already collapses repeats per (source, exception type) and hands over how many it
        // swallowed. A large count is the signal that a failure is systematic rather than a one-off,
        // so it belongs in the message — and the desktop must not add a second throttle on top.
        var logger = new Mock<IAppLogger>();
        var device = CreateDevice();
        _ = CreateSubscribedManager(logger, device);
        var error = new IOException("Read failed.");

        device.Raise(
            d => d.ErrorOccurred += null,
            device.Object,
            new CoreDeviceErrorEventArgs(DeviceErrorSource.StreamDecode, error, suppressedCount: 417));

        logger.Verify(
            l => l.Warning(error, It.Is<string>(m => m.Contains("417", StringComparison.Ordinal))),
            Times.Once);
    }

    [TestMethod]
    public void ErrorOccurred_NamesTheDeviceAndTheSource()
    {
        // "Why am I getting no samples" is only answerable if the report says which device and which
        // stage failed; a bare exception message would leave both unknown with several devices open.
        var logger = new Mock<IAppLogger>();
        var device = CreateDevice();
        _ = CreateSubscribedManager(logger, device);
        var error = new IOException("Read failed.");

        device.Raise(
            d => d.ErrorOccurred += null,
            device.Object,
            new CoreDeviceErrorEventArgs(DeviceErrorSource.MessageConsumer, error));

        logger.Verify(
            l => l.Warning(
                error,
                It.Is<string>(m =>
                    m.Contains(DISPLAY_NAME, StringComparison.Ordinal)
                    && m.Contains(nameof(DeviceErrorSource.MessageConsumer), StringComparison.Ordinal))),
            Times.Once);
    }
    #endregion

    #region SendFailed
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void SendFailed_LogsWarningAndNotError(bool isTimeout)
    {
        // A write fails because the port closed, the device went away, or the device stopped
        // draining its receive buffer. All three are conditions of the link, never app bugs, so
        // neither the timeout nor the hard-failure variant may reach Sentry.
        var logger = new Mock<IAppLogger>();
        var device = CreateDevice();
        _ = CreateSubscribedManager(logger, device);
        Exception error = isTimeout
            ? new TimeoutException("The write timed out.")
            : new IOException("The port is closed.");

        device.Raise(
            d => d.SendFailed += null,
            device.Object,
            new CoreSendFailedEventArgs(new ScpiMessage("SYSTem:STReam:ENable 1"), error));

        logger.Verify(l => l.Warning(error, It.IsAny<string>()), Times.Once);
        logger.Verify(l => l.Error(It.IsAny<Exception>(), It.IsAny<string>()), Times.Never);
        logger.Verify(l => l.Error(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public void SendFailed_NamesTheDeviceAndTheLostCommand()
    {
        // The point of the event is knowing which command the device never got — without the verb
        // the log says only that "something" failed to send.
        var logger = new Mock<IAppLogger>();
        var device = CreateDevice();
        _ = CreateSubscribedManager(logger, device);
        var error = new IOException("The port is closed.");

        device.Raise(
            d => d.SendFailed += null,
            device.Object,
            new CoreSendFailedEventArgs(new ScpiMessage("SYSTem:STReam:ENable 1"), error));

        logger.Verify(
            l => l.Warning(
                error,
                It.Is<string>(m =>
                    m.Contains(DISPLAY_NAME, StringComparison.Ordinal)
                    && m.Contains("SYSTem:STReam:ENable", StringComparison.Ordinal))),
            Times.Once);
    }

    [TestMethod]
    public void SendFailed_DoesNotWriteTheCommandArgumentsToTheLog()
    {
        // Core's SetNetworkWifiPassword puts the user's WiFi password in the message payload. A
        // failed send of that command must still be diagnosable without DAQiFiAppLog.log ending up
        // with the plaintext password in it, so only the SCPI verb is logged.
        var logger = new Mock<IAppLogger>();
        var device = CreateDevice();
        _ = CreateSubscribedManager(logger, device);
        var error = new IOException("The port is closed.");
        var passwordCommand = ScpiMessageProducer.SetNetworkWifiPassword(WIFI_PASSWORD);

        Assert.IsTrue(
            passwordCommand.Data.Contains(WIFI_PASSWORD, StringComparison.Ordinal),
            "Precondition: Core still embeds the password in the command payload.");

        device.Raise(
            d => d.SendFailed += null,
            device.Object,
            new CoreSendFailedEventArgs(passwordCommand, error));

        logger.Verify(
            l => l.Warning(error, It.Is<string>(m => !m.Contains(WIFI_PASSWORD, StringComparison.Ordinal))),
            Times.Once);
    }
    #endregion

    #region Subscribe / unsubscribe lifetime
    [TestMethod]
    public async Task Connect_SubscribesToBackgroundFailureEvents()
    {
        // Wired at the same point ConnectionLost is, so a connected device's background failures are
        // observed for the whole time it is connected and no earlier (the Core device behind these
        // events does not exist until Connect succeeds).
        var logger = new Mock<IAppLogger>();
        var device = CreateDevice();
        device.Setup(d => d.Connect()).Returns(true);
        var manager = new ConnectionManager(logger.Object);

        await manager.Connect(device.Object);

        var error = new IOException("Read failed.");
        device.Raise(
            d => d.ErrorOccurred += null,
            device.Object,
            new CoreDeviceErrorEventArgs(DeviceErrorSource.MessageConsumer, error));

        logger.Verify(l => l.Warning(error, It.IsAny<string>()), Times.Once);
    }

    [TestMethod]
    public void Disconnect_UnsubscribesFromBackgroundFailureEvents()
    {
        // The leak shape fixed in #795: an event attached at connect and never detached keeps a
        // disconnected device reporting into a handler that no longer represents anything.
        var logger = new Mock<IAppLogger>();
        var device = CreateDevice();
        var manager = CreateSubscribedManager(logger, device);

        manager.Disconnect(device.Object);
        logger.Invocations.Clear();
        RaiseBothFailureEvents(device);

        VerifyNothingWasReported(logger);
    }

    [TestMethod]
    public void Reboot_UnsubscribesFromBackgroundFailureEvents()
    {
        // Reboot is the second teardown path ConnectionLost is detached from, and it is the easier
        // one to forget: it drops the device from ConnectedDevices without going through Disconnect.
        var logger = new Mock<IAppLogger>();
        var device = CreateDevice();
        var manager = CreateSubscribedManager(logger, device);

        manager.Reboot(device.Object);
        logger.Invocations.Clear();
        RaiseBothFailureEvents(device);

        VerifyNothingWasReported(logger);
    }
    #endregion

    #region Helpers
    private static void RaiseBothFailureEvents(Mock<IStreamingDevice> device)
    {
        device.Raise(
            d => d.ErrorOccurred += null,
            device.Object,
            new CoreDeviceErrorEventArgs(DeviceErrorSource.MessageConsumer, new IOException("Read failed.")));
        device.Raise(
            d => d.SendFailed += null,
            device.Object,
            new CoreSendFailedEventArgs(new ScpiMessage("SYSTem:STReam:ENable 1"), new IOException("Write failed.")));
    }

    private static void VerifyNothingWasReported(Mock<IAppLogger> logger)
    {
        logger.Verify(l => l.Warning(It.IsAny<Exception>(), It.IsAny<string>()), Times.Never);
        logger.Verify(l => l.Warning(It.IsAny<string>()), Times.Never);
        logger.Verify(l => l.Error(It.IsAny<Exception>(), It.IsAny<string>()), Times.Never);
        logger.Verify(l => l.Error(It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// Builds a connection manager with the given sink and attaches it to <paramref name="device"/>
    /// through the same private wiring <see cref="ConnectionManager.Connect"/> uses, without paying
    /// that method's post-connect settle delay in every test. The connect path itself is covered by
    /// <see cref="Connect_SubscribesToBackgroundFailureEvents"/>.
    /// </summary>
    private static ConnectionManager CreateSubscribedManager(Mock<IAppLogger> logger, Mock<IStreamingDevice> device)
    {
        var manager = new ConnectionManager(logger.Object);
        var subscribe = typeof(ConnectionManager).GetMethod(
            "SubscribeDeviceEvents",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(subscribe, "SubscribeDeviceEvents not found.");
        subscribe.Invoke(manager, [device.Object]);
        return manager;
    }

    private static Mock<IStreamingDevice> CreateDevice()
    {
        var device = new Mock<IStreamingDevice>();
        device.SetupGet(d => d.ConnectionType).Returns(ConnectionType.Usb);
        device.SetupGet(d => d.Name).Returns("Device-805");
        device.SetupGet(d => d.DeviceDisplayName).Returns(DISPLAY_NAME);
        device.SetupGet(d => d.DeviceSerialNo).Returns(string.Empty);
        device.SetupGet(d => d.MacAddress).Returns(string.Empty);
        return device;
    }
    #endregion
}
