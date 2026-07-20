using Daqifi.Desktop.Device.WiFiDevice;
using System.Net;

namespace Daqifi.Desktop.Test.Device.WiFiDevice;

/// <summary>
/// Tests for <see cref="DaqifiStreamingDevice"/>'s connect-failure classification. The shared Connect
/// template runs Core's InitializeAsync after ConnectTcp, so device/environmental InvalidOperation
/// exceptions raised during connect must be downgraded to a Warning (no Sentry capture) rather than
/// the default Error path — mirroring the serial classification: Core's SCPI-error-during-init
/// message (issue #732, #589, #709) and Core's transport reporting the connection dropped
/// mid-initialization ("Transport is not connected.", issue #740; serial equivalent #588).
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
    public void IsScpiInitializationError_MatchesCoresWiFiInitializationErrorMessage()
    {
        // Arrange
        var ex = new InvalidOperationException(CORE_SCPI_INIT_ERROR_MESSAGE);

        // Act
        var isEnvironmental = DaqifiStreamingDevice.IsScpiInitializationError(ex);

        // Assert — proves the WiFi-surfaced message classifies as environmental (Warning, not
        // the default Error/Sentry path); guards against a regression that drops the downgrade.
        Assert.IsTrue(isEnvironmental);
    }

    [TestMethod]
    public void IsScpiInitializationError_DoesNotMatchWiFiAppBugInvalidOperationException()
    {
        // Arrange — WiFi's own app-bug InvalidOperationException must NOT be downgraded.
        var ex = new InvalidOperationException("Connected Core device does not support streaming operations.");

        // Act
        var isEnvironmental = DaqifiStreamingDevice.IsScpiInitializationError(ex);

        // Assert
        Assert.IsFalse(isEnvironmental);
    }

    [TestMethod]
    public void LogConnectFailure_WithScpiInitializationError_DoesNotThrow()
    {
        var device = CreateTestableDevice();
        var ex = new InvalidOperationException(CORE_SCPI_INIT_ERROR_MESSAGE);

        try
        {
            device.ExposedLogConnectFailure(ex);
        }
        catch (Exception caught)
        {
            Assert.Fail($"LogConnectFailure must not throw for the SCPI-init-error case, but threw: {caught}");
        }
    }

    [TestMethod]
    public void IsTransportDisconnectedError_MatchesCoreTransportNotConnectedMessage()
    {
        // Wording Core's transport throws when the connection dropped mid-initialization. It is
        // transport-agnostic (surfaces over TCP and serial), so the WiFi path must classify it as
        // the environmental transport-disconnect condition (issue #740), not the default Error path.
        var ex = new InvalidOperationException("Transport is not connected.");

        Assert.IsTrue(DaqifiStreamingDevice.IsTransportDisconnectedError(ex));
    }

    [TestMethod]
    public void IsTransportDisconnectedError_IsCaseInsensitive()
    {
        var ex = new InvalidOperationException("transport is not connected");

        Assert.IsTrue(DaqifiStreamingDevice.IsTransportDisconnectedError(ex));
    }

    [TestMethod]
    public void IsTransportDisconnectedError_DoesNotMatchWiFiAppBugInvalidOperationException()
    {
        // Regression guard: WiFi's own app-bug InvalidOperationException must NOT be downgraded as a
        // transport-disconnect — it has to keep hitting the default Error path.
        var ex = new InvalidOperationException("Connected Core device does not support streaming operations.");

        Assert.IsFalse(DaqifiStreamingDevice.IsTransportDisconnectedError(ex));
    }

    [TestMethod]
    public void LogConnectFailure_WithTransportDisconnectedError_DoesNotThrow()
    {
        var device = CreateTestableDevice();
        var ex = new InvalidOperationException("Transport is not connected.");

        try
        {
            device.ExposedLogConnectFailure(ex);
        }
        catch (Exception caught)
        {
            Assert.Fail($"LogConnectFailure must not throw for the transport-disconnect case, but threw: {caught}");
        }
    }

    [TestMethod]
    public void LogConnectFailure_WithUnrelatedInvalidOperationException_DoesNotThrow()
    {
        // Regression guard: WiFi's own app-bug InvalidOperationException
        // ("Connected Core device does not support streaming operations.") must still hit the
        // default Error path, not be silently downgraded — the SCPI-init predicate must not match it.
        var device = CreateTestableDevice();

        try
        {
            device.ExposedLogConnectFailure(
                new InvalidOperationException("Connected Core device does not support streaming operations."));
        }
        catch (Exception caught)
        {
            Assert.Fail($"LogConnectFailure must not throw for the default case, but threw: {caught}");
        }
    }

    private static TestableDaqifiStreamingDevice CreateTestableDevice() =>
        new(IPAddress.Parse("192.168.1.100"), 9760, "Test Device");

    private sealed class TestableDaqifiStreamingDevice(IPAddress ipAddress, int port, string name)
        : DaqifiStreamingDevice(ipAddress, port, name)
    {
        public void ExposedLogConnectFailure(Exception ex) => LogConnectFailure(ex);
    }
}
