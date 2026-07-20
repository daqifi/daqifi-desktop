using Daqifi.Desktop.Device.SerialDevice;

namespace Daqifi.Desktop.Test.Device;

/// <summary>
/// Tests for <see cref="SerialStreamingDevice"/>'s connect-failure classification (issue #589):
/// Core's SCPI-error-during-initialization InvalidOperationException is a device/environmental
/// condition, not an app bug, so it must be downgraded to a Warning (no Sentry capture) rather
/// than the default Error path.
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

    // The exact message Core throws from DaqifiStreamingDevice.OnDeviceInitializingAsync when the
    // "SYSTem:STReam:INTerface 0" (stream-interface -> USB) switch is rejected. This is the message
    // issue #589 and its Sentry alert DAQIFI-DESKTOP-Y are filed for; the original #589 fix only
    // matched CORE_SCPI_INIT_ERROR_MESSAGE, so this variant was still hitting the Error path.
    private const string CORE_SCPI_STREAM_INTERFACE_ERROR_MESSAGE =
        "Device returned a SCPI error while setting stream interface to USB.";

    [TestMethod]
    public void IsScpiInitializationError_MatchesCoresInitializationErrorMessage()
    {
        var ex = new InvalidOperationException(CORE_SCPI_INIT_ERROR_MESSAGE);

        Assert.IsTrue(SerialStreamingDevice.IsScpiInitializationError(ex));
    }

    [TestMethod]
    public void IsScpiInitializationError_MatchesCoresStreamInterfaceErrorMessage()
    {
        // Regression guard for #589: the actual reported message (Sentry DAQIFI-DESKTOP-Y) must be
        // classified as the environmental SCPI-init error, not fall through to the Error path.
        var ex = new InvalidOperationException(CORE_SCPI_STREAM_INTERFACE_ERROR_MESSAGE);

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
    public void LogConnectFailure_WithScpiStreamInterfaceError_DoesNotThrow()
    {
        var device = new TestableSerialStreamingDevice("COM_TEST_589");
        var ex = new InvalidOperationException(CORE_SCPI_STREAM_INTERFACE_ERROR_MESSAGE);

        try
        {
            device.ExposedLogConnectFailure(ex);
        }
        catch (Exception caught)
        {
            Assert.Fail($"LogConnectFailure must not throw for the SCPI stream-interface case, but threw: {caught}");
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
