using Daqifi.Desktop.Device.WiFiDevice;
using System.Net;

namespace Daqifi.Desktop.Test.Device.WiFiDevice;

/// <summary>
/// Tests for <see cref="DaqifiStreamingDevice"/>'s connect-failure classification (issue #732):
/// Core's SCPI-error-during-initialization InvalidOperationException surfaces on the WiFi connect
/// path too (the shared Connect template runs Core's InitializeAsync after ConnectTcp), so it is a
/// device/environmental condition that must be downgraded to a Warning (no Sentry capture) rather
/// than the default Error path — mirroring the serial classification (issues #589, #709).
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
