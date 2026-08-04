using System.ComponentModel;
using CoreDeviceErrorEventArgs = Daqifi.Core.Device.DeviceErrorEventArgs;
using CoreSendFailedEventArgs = Daqifi.Core.Communication.Producers.MessageSendFailedEventArgs<string>;

namespace Daqifi.Desktop.Device;

public interface IDevice : INotifyPropertyChanged
{
    int Id { get; set; }

    string Name { get; set; }

    /// <summary>
    /// Connects to the streamingDevice.
    /// </summary>
    /// <returns>True if successfully connected</returns>
    bool Connect();

    /// <summary>
    /// Disconnects from the streamingDevice
    /// </summary>
    /// <returns>True if successfully disconnected</returns>
    bool Disconnect();

    /// <summary>
    /// Reboots the streamingDevice
    /// </summary>
    void Reboot();

    /// <summary>
    /// Raised when the device's connection drops unexpectedly (not via an explicit
    /// <see cref="Disconnect"/> call) — e.g. reboot, unplug, WiFi/TCP drop, or
    /// firmware-flash re-enumeration. Subscribers should tear down their reference to this
    /// device and inform the user; the wrapper's own state is already updated by the time
    /// this fires.
    /// </summary>
    event EventHandler<ConnectionLostEventArgs>? ConnectionLost;

    /// <summary>
    /// Raised when Core reports a failure on one of the device's background threads — a read
    /// from the transport stream, a parse, a dispatch to a subscriber, or the decode of a
    /// single streaming frame (issue #805; daqifi-core#378). Purely observational: Core does
    /// not tear the connection down, change status, or stop the stream because of it, and a
    /// genuinely dead link still arrives separately as <see cref="ConnectionLost"/>.
    /// </summary>
    /// <remarks>
    /// Core throttles raises per (source, exception type) and reports how many like failures it
    /// collapsed in <c>SuppressedCount</c>, so subscribers must not add a throttle of their own.
    /// Raised on a Core background thread, so handlers must be thread-safe and cheap.
    /// Subscriptions are only meaningful while the device is connected: subscribe after a
    /// successful <see cref="Connect"/> and unsubscribe before <see cref="Disconnect"/>, exactly
    /// as <see cref="ConnectionLost"/> is handled in <c>ConnectionManager</c>.
    /// </remarks>
    event EventHandler<CoreDeviceErrorEventArgs>? ErrorOccurred;

    /// <summary>
    /// Raised when a message queued for this device fails to write to it (issue #805;
    /// daqifi-core#413). Sending is fire-and-forget, so before this event a SCPI command could
    /// fail to reach the device with no error and no log, leaving the app's idea of device state
    /// silently diverging from the device's.
    /// </summary>
    /// <remarks>
    /// Purely observational — the producer keeps draining its remaining queue. Raised on the
    /// producer's background thread, and subject to the same subscribe-after-connect /
    /// unsubscribe-before-disconnect lifetime as <see cref="ErrorOccurred"/>.
    /// </remarks>
    event EventHandler<CoreSendFailedEventArgs>? SendFailed;
}
