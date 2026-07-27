using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device.WiFiDevice;
using Moq;
using System.Net;
using ScpiInitializationErrorException = Daqifi.Core.Device.ScpiInitializationErrorException;

namespace Daqifi.Desktop.Test.Device.WiFiDevice;

/// <summary>
/// Tests for <see cref="DaqifiStreamingDevice"/>'s connect-failure classification. The shared Connect
/// template runs Core's InitializeAsync after ConnectTcp, so device/environmental failures raised
/// during connect must be downgraded to a Warning (no Sentry capture) rather than the default Error
/// path — mirroring the serial classification: Core's SCPI-error-during-init (issues #732, #589,
/// #709, #775) and Core's transport reporting the connection dropped mid-initialization
/// ("Transport is not connected.", issue #740; serial equivalent #588).
/// <para>
/// The <c>LogConnectFailure_*</c> cases assert the log level actually used, via an injected
/// <see cref="IAppLogger"/> — the earlier "did not throw" assertions were true of every switch arm,
/// so they stayed green while the SCPI arm was unreachable (issue #775).
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
public class DaqifiStreamingDeviceLogConnectFailureTests
{
    // Exact wording Daqifi.Core throws from DaqifiStreamingDevice.InitializeAsync() when any command
    // in its init sequence gets back a SCPI -200 execution error. Transport-agnostic: the same Core
    // init sequence runs over TCP (WiFi) and serial.
    private const string CORE_SCPI_INIT_ERROR_MESSAGE =
        "Device returned a SCPI error during initialization: -200,\"Execution error\"";

    [TestMethod]
    public void IsTransportDisconnectedError_MatchesCoreTransportNotConnectedMessage()
    {
        // Arrange — wording Core's transport throws when the connection dropped mid-initialization.
        // It is transport-agnostic (surfaces over TCP and serial), so the WiFi path must classify it
        // as the environmental transport-disconnect condition (issue #740), not the default Error path.
        var ex = new InvalidOperationException("Transport is not connected.");

        // Act
        var isEnvironmental = DaqifiStreamingDevice.IsTransportDisconnectedError(ex);

        // Assert
        Assert.IsTrue(isEnvironmental);
    }

    [TestMethod]
    public void IsTransportDisconnectedError_IsCaseInsensitive()
    {
        // Arrange
        var ex = new InvalidOperationException("transport is not connected");

        // Act
        var isEnvironmental = DaqifiStreamingDevice.IsTransportDisconnectedError(ex);

        // Assert
        Assert.IsTrue(isEnvironmental);
    }

    [TestMethod]
    public void IsTransportDisconnectedError_DoesNotMatchWiFiAppBugInvalidOperationException()
    {
        // Arrange — WiFi's own app-bug InvalidOperationException must NOT be downgraded as a
        // transport-disconnect; it has to keep hitting the default Error path.
        var ex = new InvalidOperationException("Connected Core device does not support streaming operations.");

        // Act
        var isEnvironmental = DaqifiStreamingDevice.IsTransportDisconnectedError(ex);

        // Assert
        Assert.IsFalse(isEnvironmental);
    }

    [TestMethod]
    public void LogConnectFailure_WithScpiInitializationError_LogsWarningAndNotError()
    {
        // Arrange — Core 1.3.0 throws its typed ScpiInitializationErrorException (daqifi-core#317),
        // which derives from Exception rather than InvalidOperationException.
        var logger = new Mock<IAppLogger>();
        var device = CreateTestableDevice(logger.Object);
        var ex = CreateScpiInitializationError(CORE_SCPI_INIT_ERROR_MESSAGE);

        // Act
        device.ExposedLogConnectFailure(ex);

        // Assert — environmental condition: Warning carrying the exception detail, never the Error
        // (Sentry) path. Regression guard for #775.
        logger.Verify(l => l.Warning(ex, It.IsAny<string>()), Times.Once);
        logger.Verify(l => l.Error(It.IsAny<Exception>(), It.IsAny<string>()), Times.Never);
        logger.Verify(l => l.Error(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public void LogConnectFailure_WithTransportDisconnectedError_LogsWarningAndNotError()
    {
        // Arrange — the connection dropping mid-initialization (issue #740) stays a Warning.
        var logger = new Mock<IAppLogger>();
        var device = CreateTestableDevice(logger.Object);
        var ex = new InvalidOperationException("Transport is not connected.");

        // Act
        device.ExposedLogConnectFailure(ex);

        // Assert
        logger.Verify(l => l.Warning(ex, It.IsAny<string>()), Times.Once);
        logger.Verify(l => l.Error(It.IsAny<Exception>(), It.IsAny<string>()), Times.Never);
        logger.Verify(l => l.Error(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public void LogConnectFailure_WithUnrelatedInvalidOperationException_LogsError()
    {
        // Arrange — guard in the other direction: WiFi's own app-bug InvalidOperationException must
        // still reach the Error (Sentry) path, not be silently downgraded.
        var logger = new Mock<IAppLogger>();
        var device = CreateTestableDevice(logger.Object);
        var ex = new InvalidOperationException("Connected Core device does not support streaming operations.");

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

    private static TestableDaqifiStreamingDevice CreateTestableDevice(IAppLogger appLogger) =>
        new(IPAddress.Parse("192.168.1.100"), 9760, "Test Device", appLogger);

    private sealed class TestableDaqifiStreamingDevice : DaqifiStreamingDevice
    {
        public TestableDaqifiStreamingDevice(IPAddress ipAddress, int port, string name, IAppLogger appLogger)
            : base(ipAddress, port, name)
        {
            AppLogger = appLogger;
        }

        public void ExposedLogConnectFailure(Exception ex) => LogConnectFailure(ex);
    }
}
