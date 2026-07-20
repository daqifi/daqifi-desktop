using Daqifi.Desktop.Device.SerialDevice;

namespace Daqifi.Desktop.Test.Device;

/// <summary>
/// Tests for <see cref="SerialStreamingDevice"/>'s connect-failure classification. Certain
/// InvalidOperationExceptions raised during connect are device/environmental conditions, not app
/// bugs, so they must be downgraded to a Warning (no Sentry capture) rather than the default Error
/// path: Core's SCPI-error-during-initialization message (issue #589) and the serial transport
/// reporting the COM port closed mid-initialization (issue #588).
/// </summary>
[TestClass]
public class SerialStreamingDeviceLogConnectFailureTests
{
    // Exact wording Daqifi.Core 1.1.1 throws from DaqifiStreamingDevice.InitializeAsync()
    // (verified against the NuGet package's DLL strings) when any command in its init sequence
    // gets back a SCPI -200 execution error — e.g. "SYSTem:STReam:INTerface 0" (setting the
    // stream interface to USB) rejected because firmware persisted WiFi as the last interface.
    private const string CORE_SCPI_INIT_ERROR_MESSAGE =
        "Device returned a SCPI error during initialization: -200,\"Execution error\"";

    [TestMethod]
    public void IsScpiInitializationError_MatchesCoresInitializationErrorMessage()
    {
        var ex = new InvalidOperationException(CORE_SCPI_INIT_ERROR_MESSAGE);

        Assert.IsTrue(SerialStreamingDevice.IsScpiInitializationError(ex));
    }

    [TestMethod]
    public void IsScpiInitializationError_IsCaseInsensitive()
    {
        var ex = new InvalidOperationException("device returned a scpi error during initialization: whatever");

        Assert.IsTrue(SerialStreamingDevice.IsScpiInitializationError(ex));
    }

    [TestMethod]
    public void IsScpiInitializationError_DoesNotMatchUnrelatedInvalidOperationException()
    {
        // Regression guard: an unrelated InvalidOperationException bug must still hit the
        // default Error path instead of being silently downgraded.
        var ex = new InvalidOperationException("Transport exploded.");

        Assert.IsFalse(SerialStreamingDevice.IsScpiInitializationError(ex));
    }

    [TestMethod]
    public void IsScpiInitializationError_DoesNotMatchUnrelatedScpiErrorMention()
    {
        // Regression guard: the predicate matches Core's full known prefix, not the bare
        // substring "SCPI error" — an unrelated failure that happens to mention a SCPI error in
        // some other context must not be misclassified as the initialization error.
        var ex = new InvalidOperationException("SCPI error while doing something unrelated to initialization.");

        Assert.IsFalse(SerialStreamingDevice.IsScpiInitializationError(ex));
    }

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
    public void LogConnectFailure_WithTransportClosedError_DoesNotThrow()
    {
        var device = new TestableSerialStreamingDevice("COM_TEST_588");
        var ex = new InvalidOperationException("The BaseStream is only available when the port is open.");

        try
        {
            device.ExposedLogConnectFailure(ex);
        }
        catch (Exception caught)
        {
            Assert.Fail($"LogConnectFailure must not throw for the transport-closed case, but threw: {caught}");
        }
    }

    [TestMethod]
    public void LogConnectFailure_WithScpiInitializationError_DoesNotThrow()
    {
        var device = new TestableSerialStreamingDevice("COM_TEST_589");
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
    public void LogConnectFailure_WithUnrelatedException_DoesNotThrow()
    {
        var device = new TestableSerialStreamingDevice("COM_TEST_589");

        try
        {
            device.ExposedLogConnectFailure(new InvalidOperationException("Transport exploded."));
        }
        catch (Exception caught)
        {
            Assert.Fail($"LogConnectFailure must not throw for the default case, but threw: {caught}");
        }
    }

    private sealed class TestableSerialStreamingDevice(string portName) : SerialStreamingDevice(portName)
    {
        public void ExposedLogConnectFailure(Exception ex) => LogConnectFailure(ex);
    }
}
