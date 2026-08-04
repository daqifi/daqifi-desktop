using System.Reflection;
using Daqifi.Core.Communication.Messages;
using Daqifi.Desktop.Device;
using CoreDeviceErrorEventArgs = Daqifi.Core.Device.DeviceErrorEventArgs;
using CoreStreamingDevice = Daqifi.Core.Device.DaqifiStreamingDevice;
using DeviceErrorSource = Daqifi.Core.Device.DeviceErrorSource;

namespace Daqifi.Desktop.Test.Device;

/// <summary>
/// Tests for the desktop wrapper's re-exposure of Core's background-failure events (issue #805).
/// <c>ConnectionManager</c> routes these to the app log, but only if the wrapper actually attaches
/// to the Core device when someone subscribes and detaches when the last one leaves — the half of
/// the wiring a mocked <c>IStreamingDevice</c> cannot prove.
/// </summary>
[TestClass]
public class AbstractStreamingDeviceDiagnosticsTests
{
    [TestMethod]
    public void ErrorOccurred_ForwardsCoreFailuresWithTheDesktopDeviceAsSender()
    {
        // The sender must be the desktop wrapper, not the Core device: a log line has to name the
        // device the way the user sees it, and only the wrapper knows DeviceDisplayName.
        using var device = new DiagnosticsTestDevice();
        object? capturedSender = null;
        CoreDeviceErrorEventArgs? capturedArgs = null;
        device.ErrorOccurred += (sender, e) =>
        {
            capturedSender = sender;
            capturedArgs = e;
        };
        var error = new IOException("Read failed.");

        device.RaiseCoreDeviceError(DeviceErrorSource.MessageConsumer, error);

        Assert.AreSame(device, capturedSender, "Handlers should see the desktop device, not the Core one.");
        Assert.IsNotNull(capturedArgs);
        Assert.AreEqual(DeviceErrorSource.MessageConsumer, capturedArgs.Source);
        Assert.AreSame(error, capturedArgs.Error);
        Assert.IsTrue(IsAttachedToCore(device));
    }

    [TestMethod]
    public void ErrorOccurred_AfterUnsubscribe_DeliversNothing()
    {
        // The no-leak half: once the last subscriber detaches, the wrapper must release its own
        // subscription on the Core device rather than keeping a disconnected device reporting into
        // handlers nobody is listening with (the #795 shape).
        using var device = new DiagnosticsTestDevice();
        var deliveries = 0;
        void Handler(object? sender, CoreDeviceErrorEventArgs e) => deliveries++;

        device.ErrorOccurred += Handler;
        device.ErrorOccurred -= Handler;

        // A different exception type so Core's per-(source, type) throttle cannot be what silences
        // this raise — the test must fail for the right reason.
        device.RaiseCoreDeviceError(DeviceErrorSource.StreamDecode, new InvalidOperationException("Decode failed."));

        Assert.AreEqual(0, deliveries, "An unsubscribed handler must not keep receiving Core failures.");
        Assert.IsFalse(IsAttachedToCore(device),
            "The wrapper should have released its Core subscription once no subscriber was left.");
    }

    [TestMethod]
    public void ErrorOccurred_WithTwoSubscribers_KeepsDeliveringAfterOneDetaches()
    {
        // One Core subscription is shared by every desktop subscriber, so releasing it must wait for
        // the last one — otherwise one component unsubscribing silences everybody else.
        using var device = new DiagnosticsTestDevice();
        var first = 0;
        var second = 0;
        void FirstHandler(object? sender, CoreDeviceErrorEventArgs e) => first++;
        void SecondHandler(object? sender, CoreDeviceErrorEventArgs e) => second++;

        device.ErrorOccurred += FirstHandler;
        device.ErrorOccurred += SecondHandler;
        device.ErrorOccurred -= FirstHandler;

        device.RaiseCoreDeviceError(DeviceErrorSource.MessageConsumer, new IOException("Read failed."));

        Assert.AreEqual(0, first);
        Assert.AreEqual(1, second, "The remaining subscriber must still receive Core failures.");
        Assert.IsTrue(IsAttachedToCore(device));
    }

    /// <summary>
    /// True while the wrapper holds a live forwarding subscription on its Core device. Read from the
    /// private field because Core's <c>ErrorOccurred</c> is a non-virtual field-like event, so
    /// nothing outside <c>DaqifiDevice</c> can inspect its invocation list.
    /// </summary>
    private static bool IsAttachedToCore(AbstractStreamingDevice device)
    {
        var field = typeof(AbstractStreamingDevice).GetField(
            "_diagnosticsSource",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "_diagnosticsSource not found.");
        return field.GetValue(device) != null;
    }

    /// <summary>
    /// A wrapper backed by a Core device that can raise <c>ErrorOccurred</c> on demand, standing in
    /// for the read/decode threads that raise it in production.
    /// </summary>
    private sealed class DiagnosticsTestDevice : AbstractStreamingDevice, IDisposable
    {
        private readonly ErrorRaisingCoreDevice _coreDevice = new();

        public DiagnosticsTestDevice()
        {
            CoreDevice = _coreDevice;
        }

        public override ConnectionType ConnectionType => ConnectionType.Usb;

        public void RaiseCoreDeviceError(DeviceErrorSource source, Exception error) =>
            _coreDevice.RaiseError(source, error);

        public override bool Connect() => true;

        public override bool Disconnect() => true;

        public override bool Write(string command) => true;

        protected override void SendMessage(IOutboundMessage<string> message)
        {
        }

        // The Core device built in the constructor is owned by this fixture; disposing it keeps the
        // suite from leaking one per test (CA1001).
        public void Dispose() => _coreDevice.Dispose();
    }

    private sealed class ErrorRaisingCoreDevice() : CoreStreamingDevice("DiagnosticsTestDevice")
    {
        public void RaiseError(DeviceErrorSource source, Exception error) => RaiseDeviceError(source, error);
    }
}
