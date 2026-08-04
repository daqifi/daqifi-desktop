using CoreDeviceErrorEventArgs = Daqifi.Core.Device.DeviceErrorEventArgs;
using CoreSendFailedEventArgs = Daqifi.Core.Communication.Producers.MessageSendFailedEventArgs<string>;
using CoreStreamingDevice = Daqifi.Core.Device.DaqifiStreamingDevice;

namespace Daqifi.Desktop.Device;

/// <summary>
/// Re-exposes Core's background-failure events (<c>ErrorOccurred</c>, <c>SendFailed</c>) on the
/// desktop wrapper so <c>ConnectionManager</c> can route them to the app log with the right
/// severity (issue #805). Before Core 1.4.0 these failures had nowhere to go at all: a read-loop
/// fault or a producer write that never reached the device was indistinguishable from silence.
/// </summary>
/// <remarks>
/// <para>
/// Forwarding rather than passing the Core event object straight through is deliberate: handlers
/// receive <c>this</c> as the sender, so a log line can name the device the way the user sees it
/// (<see cref="DeviceDisplayName"/>) instead of the Core-internal object.
/// </para>
/// <para>
/// The Core subscription is attached on the first desktop subscriber and released on the last, and
/// the attached instance is remembered so the release always targets the same Core device even if
/// <c>CoreDevice</c> has since been replaced by a reconnect. This late binding is what lets the
/// wiring live entirely in this file: <c>CoreDevice</c> does not exist until <c>Connect()</c>
/// creates it, and the wrapper's existing <c>SubscribeCoreDeviceEvents</c> runs before any desktop
/// subscriber has appeared.
/// </para>
/// </remarks>
public abstract partial class AbstractStreamingDevice
{
    #region Private Fields
    /// <summary>
    /// Guards the handler lists and the attach/detach pair below. Core raises both events from
    /// background threads while <c>ConnectionManager</c> subscribes and unsubscribes from the UI
    /// thread, so the bookkeeping cannot be left to unsynchronized delegate assignment.
    /// </summary>
    private readonly object _diagnosticsSync = new();

    /// <summary>
    /// The Core device this wrapper's forwarding handlers are currently attached to, or null when
    /// nothing is attached. Held separately from <c>CoreDevice</c> so detaching cannot miss the
    /// instance it attached to.
    /// </summary>
    private CoreStreamingDevice? _diagnosticsSource;

    private EventHandler<CoreDeviceErrorEventArgs>? _errorOccurred;
    private EventHandler<CoreSendFailedEventArgs>? _sendFailed;
    #endregion

    #region Events
    /// <inheritdoc />
    public event EventHandler<CoreDeviceErrorEventArgs>? ErrorOccurred
    {
        add
        {
            if (value == null) { return; }

            lock (_diagnosticsSync)
            {
                AttachDiagnostics();
                _errorOccurred += value;
            }
        }
        remove
        {
            if (value == null) { return; }

            lock (_diagnosticsSync)
            {
                _errorOccurred -= value;
                DetachDiagnosticsIfUnobserved();
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<CoreSendFailedEventArgs>? SendFailed
    {
        add
        {
            if (value == null) { return; }

            lock (_diagnosticsSync)
            {
                AttachDiagnostics();
                _sendFailed += value;
            }
        }
        remove
        {
            if (value == null) { return; }

            lock (_diagnosticsSync)
            {
                _sendFailed -= value;
                DetachDiagnosticsIfUnobserved();
            }
        }
    }
    #endregion

    #region Private Methods
    /// <summary>
    /// Attaches this wrapper's forwarding handlers to the current Core device, moving them off any
    /// previously attached instance first. A no-op while the device is disconnected
    /// (<c>CoreDevice</c> is null) — there is no background pipeline to report failures yet.
    /// </summary>
    private void AttachDiagnostics()
    {
        var coreDevice = CoreDevice;
        if (ReferenceEquals(_diagnosticsSource, coreDevice))
        {
            return;
        }

        DetachDiagnostics();

        if (coreDevice == null)
        {
            return;
        }

        coreDevice.ErrorOccurred += OnCoreErrorOccurred;
        coreDevice.SendFailed += OnCoreSendFailed;
        _diagnosticsSource = coreDevice;
    }

    /// <summary>
    /// Releases the Core subscription once no desktop subscriber is left, so a disconnected
    /// device's Core instance is not kept reporting into handlers nobody is listening with.
    /// </summary>
    private void DetachDiagnosticsIfUnobserved()
    {
        if (_errorOccurred == null && _sendFailed == null)
        {
            DetachDiagnostics();
        }
    }

    private void DetachDiagnostics()
    {
        if (_diagnosticsSource == null)
        {
            return;
        }

        _diagnosticsSource.ErrorOccurred -= OnCoreErrorOccurred;
        _diagnosticsSource.SendFailed -= OnCoreSendFailed;
        _diagnosticsSource = null;
    }

    private void OnCoreErrorOccurred(object? sender, CoreDeviceErrorEventArgs e)
    {
        _errorOccurred?.Invoke(this, e);
    }

    private void OnCoreSendFailed(object? sender, CoreSendFailedEventArgs e)
    {
        _sendFailed?.Invoke(this, e);
    }
    #endregion
}
