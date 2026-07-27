using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device.SerialDevice;
using Moq;
using ScpiInitializationErrorException = Daqifi.Core.Device.ScpiInitializationErrorException;

namespace Daqifi.Desktop.Test.Device;

/// <summary>
/// Tests for <see cref="SerialStreamingDevice"/>'s connect-failure classification. Certain failures
/// raised during connect are device/environmental conditions, not app bugs, so they must be
/// downgraded to a Warning (which does NOT capture to Sentry) rather than the default Error path:
/// Core's SCPI-error-during-initialization (issues #589/#775) and the serial transport reporting the
/// COM port closed mid-initialization (issue #588).
/// <para>
/// The <c>LogConnectFailure_*</c> cases assert the log level actually used, via an injected
/// <see cref="IAppLogger"/>. That is the whole point: the earlier versions of these tests only
/// asserted "did not throw", which is true of every switch arm, so they stayed green while the SCPI
/// arm was unreachable (issue #775) and the condition was silently reaching Sentry.
/// </para>
/// <para>
/// Each case verifies BOTH <see cref="IAppLogger.Error(Exception, string)"/> and
/// <see cref="IAppLogger.Error(string)"/>, because both capture to Sentry — the exception overload
/// via <c>SentrySdk.CaptureException(ex, ...)</c> and the message-only overload via a synthesized
/// <c>AppLogErrorException</c>. Guarding only the exception overload would leave a live Sentry path
/// a regression could take with these tests still green.
/// </para>
/// </summary>
[TestClass]
public class SerialStreamingDeviceLogConnectFailureTests
{
    // Exact wording Daqifi.Core throws from DaqifiStreamingDevice.InitializeAsync() when any command
    // in its init sequence gets back a SCPI -200 execution error — e.g. "SYSTem:STReam:INTerface 0"
    // (setting the stream interface to USB) rejected because firmware persisted WiFi as the last
    // interface.
    private const string CORE_SCPI_INIT_ERROR_MESSAGE =
        "Device returned a SCPI error during initialization: -200,\"Execution error\"";

    // The exact message Core throws from DaqifiStreamingDevice.OnDeviceInitializingAsync when the
    // "SYSTem:STReam:INTerface 0" (stream-interface -> USB) switch is rejected. This is the message
    // issue #589 and its Sentry alert DAQIFI-DESKTOP-Y are filed for.
    private const string CORE_SCPI_STREAM_INTERFACE_ERROR_MESSAGE =
        "Device returned a SCPI error while setting stream interface to USB.";

    [TestMethod]
    public void IsTransportClosedError_MatchesDotNetBaseStreamMessage()
    {
        // Exact wording .NET's SerialPort.BaseStream getter throws when the port has closed —
        // the message captured in Sentry issue #588.
        var ex = new InvalidOperationException("The BaseStream is only available when the port is open.");

        Assert.IsTrue(SerialStreamingDevice.IsTransportClosedError(ex));
    }

    [TestMethod]
    public void IsTransportClosedError_MatchesCoreTransportNotConnectedMessage()
    {
        // Wording Core's SerialStreamTransport throws when its SerialPort reference is null.
        var ex = new InvalidOperationException("Transport is not connected.");

        Assert.IsTrue(SerialStreamingDevice.IsTransportClosedError(ex));
    }

    [TestMethod]
    public void IsTransportClosedError_IsCaseInsensitive()
    {
        var ex = new InvalidOperationException("the basestream is only available when the port is open");

        Assert.IsTrue(SerialStreamingDevice.IsTransportClosedError(ex));
    }

    [TestMethod]
    public void IsTransportClosedError_DoesNotMatchUnrelatedInvalidOperationException()
    {
        // Regression guard: an unrelated InvalidOperationException bug must still hit the
        // default Error path instead of being silently downgraded.
        var ex = new InvalidOperationException("Transport exploded.");

        Assert.IsFalse(SerialStreamingDevice.IsTransportClosedError(ex));
    }

    [TestMethod]
    public void LogConnectFailure_WithScpiInitializationError_LogsWarningAndNotError()
    {
        // Arrange — Core 1.3.0 throws its typed ScpiInitializationErrorException (daqifi-core#317)
        // from InitializeAsync, NOT an InvalidOperationException.
        var logger = new Mock<IAppLogger>();
        var device = new TestableSerialStreamingDevice("COM_TEST_589", logger.Object);
        var ex = CreateScpiInitializationError(CORE_SCPI_INIT_ERROR_MESSAGE);

        // Act
        device.ExposedLogConnectFailure(ex);

        // Assert — environmental condition: Warning carrying the exception detail, and never the
        // Error path (which is what captures to Sentry). Regression guard for #775.
        logger.Verify(l => l.Warning(ex, It.IsAny<string>()), Times.Once);
        logger.Verify(l => l.Error(It.IsAny<Exception>(), It.IsAny<string>()), Times.Never);
        logger.Verify(l => l.Error(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public void LogConnectFailure_WithScpiStreamInterfaceError_LogsWarningAndNotError()
    {
        // Arrange — the stream-interface variant (Sentry DAQIFI-DESKTOP-Y) is the same typed
        // exception, thrown from Core's OnDeviceInitializingAsync.
        var logger = new Mock<IAppLogger>();
        var device = new TestableSerialStreamingDevice("COM_TEST_589", logger.Object);
        var ex = CreateScpiInitializationError(CORE_SCPI_STREAM_INTERFACE_ERROR_MESSAGE);

        // Act
        device.ExposedLogConnectFailure(ex);

        // Assert
        logger.Verify(l => l.Warning(ex, It.IsAny<string>()), Times.Once);
        logger.Verify(l => l.Error(It.IsAny<Exception>(), It.IsAny<string>()), Times.Never);
        logger.Verify(l => l.Error(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public void LogConnectFailure_WithTransportClosedError_LogsWarningAndNotError()
    {
        // Arrange — the COM port closing mid-initialization (issue #588) stays a Warning. Core's
        // TransportNotConnectedException does derive from InvalidOperationException, so this arm
        // was never affected by #775; asserted here so the two classifications can't drift.
        var logger = new Mock<IAppLogger>();
        var device = new TestableSerialStreamingDevice("COM_TEST_588", logger.Object);
        var ex = new InvalidOperationException("The BaseStream is only available when the port is open.");

        // Act
        device.ExposedLogConnectFailure(ex);

        // Assert
        logger.Verify(l => l.Warning(ex, It.IsAny<string>()), Times.Once);
        logger.Verify(l => l.Error(It.IsAny<Exception>(), It.IsAny<string>()), Times.Never);
        logger.Verify(l => l.Error(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public void LogConnectFailure_WithUnrelatedException_LogsError()
    {
        // Arrange — the guard in the other direction: a genuine app bug must still reach the Error
        // (Sentry) path. Without this, "always log a Warning" would pass every other case here.
        var logger = new Mock<IAppLogger>();
        var device = new TestableSerialStreamingDevice("COM_TEST_589", logger.Object);
        var ex = new InvalidOperationException("Transport exploded.");

        // Act
        device.ExposedLogConnectFailure(ex);

        // Assert
        logger.Verify(l => l.Error(ex, It.IsAny<string>()), Times.Once);
        logger.Verify(l => l.Warning(It.IsAny<Exception>(), It.IsAny<string>()), Times.Never);

        // ...and via the exception-carrying overload specifically: the message-only Error(string)
        // synthesizes an AppLogErrorException, which would strand the real stack trace out of Sentry.
        logger.Verify(l => l.Error(It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// Builds the exception Core 1.3.0 actually throws for a SCPI initialization failure: the typed
    /// <c>ScpiInitializationErrorException</c> (daqifi-core#317), which carries the raw device reply
    /// and the parsed SCPI error alongside the message. It derives from <see cref="Exception"/>, not
    /// <see cref="InvalidOperationException"/> — the whole reason issue #775 existed.
    /// </summary>
    private static ScpiInitializationErrorException CreateScpiInitializationError(string message) =>
        new(message, ["-200,\"Execution error\""], "-200,\"Execution error\"");

    private sealed class TestableSerialStreamingDevice : SerialStreamingDevice
    {
        public TestableSerialStreamingDevice(string portName, IAppLogger appLogger)
            : base(portName)
        {
            AppLogger = appLogger;
        }

        public void ExposedLogConnectFailure(Exception ex) => LogConnectFailure(ex);
    }
}
